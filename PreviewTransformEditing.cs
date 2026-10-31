using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private sealed record PreviewVisual(EditClip Clip, Image Image, ClipBounds Bounds, double SourceWidth, double SourceHeight);
    private sealed record PreviewDrag(uint PointerId, EditClip Original, ClipBounds OriginalBounds, Point Origin, PreviewVisual Visual, int Horizontal, int Vertical)
    {
        public bool Resize => Horizontal != 0 || Vertical != 0;
        public bool LeftCorner => Horizontal < 0;
        public bool TopCorner => Vertical < 0;
        public EditClip Candidate { get; set; } = ClipEditing.Clone(Original);
    }

    private List<PreviewVisual> _previewVisuals = [];
    private PreviewDrag? _previewDrag;
    private double PreviewScale => Math.Min(PreviewViewport.ActualWidth / _project.Video.Width, PreviewViewport.ActualHeight / _project.Video.Height);

    private Point ProjectPoint(Point point)
    {
        double scale = PreviewScale;
        double left = (PreviewViewport.ActualWidth - _project.Video.Width * scale) / 2;
        double top = (PreviewViewport.ActualHeight - _project.Video.Height * scale) / 2;
        return new Point((point.X - left) / scale, (point.Y - top) / scale);
    }

    private ClipBounds ViewportBounds(ClipBounds bounds)
    {
        double scale = PreviewScale;
        double left = (PreviewViewport.ActualWidth - _project.Video.Width * scale) / 2;
        double top = (PreviewViewport.ActualHeight - _project.Video.Height * scale) / 2;
        return new ClipBounds(left + bounds.X * scale, top + bounds.Y * scale, bounds.Width * scale, bounds.Height * scale);
    }

    private static void PlacePreviewImage(Image image, ClipBounds bounds)
    {
        image.Width = bounds.Width;
        image.Height = bounds.Height;
        Canvas.SetLeft(image, bounds.X);
        Canvas.SetTop(image, bounds.Y);
    }

    private void PreviewViewport_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTransformOverlay();
    private void UpdateTransformOverlay(ClipBounds? changingBounds = null)
    {
        if (PreviewInteraction == null)
            return;
        PreviewInteraction.Children.Clear();
        if (PausedPreview.Visibility != Visibility.Visible || PreviewScale <= 0)
            return;
        PreviewVisual? visual = _previewVisuals.FirstOrDefault(item => item.Clip.Id == _selected);
        if (visual == null)
            return;
        ClipBounds bounds = ViewportBounds(changingBounds ?? visual.Bounds);
        SolidColorBrush stroke = ObjectAppearance.Brush(visual.Clip);
        Microsoft.UI.Xaml.Shapes.Rectangle outline = new()
        {
            Width = bounds.Width,
            Height = bounds.Height,
            StrokeThickness = 2,
            Stroke = stroke,
            StrokeDashArray = new DoubleCollection { 3, 2 },
            IsHitTestVisible = false
        };
        Canvas.SetLeft(outline, bounds.X);
        Canvas.SetTop(outline, bounds.Y);
        PreviewInteraction.Children.Add(outline);
        foreach (Point point in new[]
        {
            new Point(bounds.X, bounds.Y), new Point(bounds.Right, bounds.Y),
            new Point(bounds.X, bounds.Bottom), new Point(bounds.Right, bounds.Bottom),
            new Point((bounds.X + bounds.Right) / 2, bounds.Y), new Point((bounds.X + bounds.Right) / 2, bounds.Bottom),
            new Point(bounds.X, (bounds.Y + bounds.Bottom) / 2), new Point(bounds.Right, (bounds.Y + bounds.Bottom) / 2)
        })
        {
            Border handle = new() { Width = 8, Height = 8, Background = stroke, IsHitTestVisible = false };
            Canvas.SetLeft(handle, point.X - 4);
            Canvas.SetTop(handle, point.Y - 4);
            PreviewInteraction.Children.Add(handle);
        }
    }

    private static (int Horizontal, int Vertical) PreviewEdges(Point point, ClipBounds bounds)
    {
        if (point.X < bounds.X - 8 || point.X > bounds.Right + 8 || point.Y < bounds.Y - 8 || point.Y > bounds.Bottom + 8)
            return (0, 0);
        int h = Math.Abs(point.X - bounds.X) <= 8 ? -1 : Math.Abs(point.X - bounds.Right) <= 8 ? 1 : 0;
        int v = Math.Abs(point.Y - bounds.Y) <= 8 ? -1 : Math.Abs(point.Y - bounds.Bottom) <= 8 ? 1 : 0;
        return (h, v);
    }

    private void SetPreviewCursor(int horizontal, int vertical)
    {
        InputSystemCursorShape shape = horizontal != 0 && vertical != 0
            ? horizontal == vertical ? InputSystemCursorShape.SizeNorthwestSoutheast : InputSystemCursorShape.SizeNortheastSouthwest
            : horizontal != 0 ? InputSystemCursorShape.SizeWestEast
            : vertical != 0 ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.Arrow;
        PreviewInteraction.SetCursor(shape);
    }

    private void Preview_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_previewDrag == null) SetPreviewCursor(0, 0);
    }
    private void Preview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint pointer = e.GetCurrentPoint(PreviewInteraction);
        if (!pointer.Properties.IsLeftButtonPressed || _previewDrag != null || _drag != null || _scrubPointer != null || PreviewScale <= 0)
            return;
        Pause();
        Point projectPoint = ProjectPoint(pointer.Position);
        PreviewVisual? selectedVisual = _previewVisuals.FirstOrDefault(item => item.Clip.Id == _selected);
        (int Horizontal, int Vertical) edges = selectedVisual == null ? (Horizontal: 0, Vertical: 0)
            : PreviewEdges(pointer.Position, ViewportBounds(selectedVisual.Bounds));
        bool resize = edges.Horizontal != 0 || edges.Vertical != 0;
        PreviewVisual? visual = resize ? selectedVisual : _previewVisuals.LastOrDefault(item => item.Bounds.Contains(projectPoint.X, projectPoint.Y));
        if (visual == null || !_gate.Wait(0))
            return;
        EditClip? clip = _project.Clips.FirstOrDefault(item => item.Id == visual.Clip.Id && _playhead >= item.Start && _playhead < item.Start + item.Duration);
        if (clip == null || _project.Layer(clip.Layer).Locked || !PreviewInteraction.CapturePointer(e.Pointer))
        {
            _gate.Release();
            return;
        }

        EditClip original = ClipEditing.Clone(clip);
        original.SubtitleBounds = visual.Clip.SubtitleBounds;
        _previewDrag = new PreviewDrag(e.Pointer.PointerId, original, visual.Bounds, projectPoint, visual, edges.Horizontal, edges.Vertical);
        SetPreviewCursor(edges.Horizontal, edges.Vertical);
        Select(clip);
        e.Handled = true;
    }

    private void Preview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        PreviewDrag? state = _previewDrag;
        if (state == null)
        {
            PreviewVisual? visual = _previewVisuals.FirstOrDefault(item => item.Clip.Id == _selected);
            (int Horizontal, int Vertical) edges = visual == null ? (Horizontal: 0, Vertical: 0) : PreviewEdges(e.GetCurrentPoint(PreviewInteraction).Position, ViewportBounds(visual.Bounds));
            SetPreviewCursor(edges.Horizontal, edges.Vertical);
            return;
        }
        if (state.PointerId != e.Pointer.PointerId) return;
        Point point = ProjectPoint(e.GetCurrentPoint(PreviewInteraction).Position);
        double cornerX = state.LeftCorner ? state.OriginalBounds.X : state.OriginalBounds.Right;
        double cornerY = state.TopCorner ? state.OriginalBounds.Y : state.OriginalBounds.Bottom;
        if (state.Horizontal != 0 && state.Vertical != 0)
        {
            state.Candidate = ClipGeometry.Resize(
                state.Original, state.OriginalBounds,
                cornerX + point.X - state.Origin.X,
                cornerY + point.Y - state.Origin.Y,
                state.LeftCorner, state.TopCorner);
        }
        else if (state.Resize)
        {
            state.Candidate = ClipGeometry.ResizeEdge(state.Original, state.OriginalBounds,
                point.X - state.Origin.X, point.Y - state.Origin.Y, state.Horizontal, state.Vertical);
        }
        else
        {
            state.Candidate = ClipGeometry.Move(state.Original, point.X - state.Origin.X, point.Y - state.Origin.Y);
        }
        if (!state.Resize)
        {
            state.Candidate = ClipGeometry.SnapToCanvas(state.Candidate, state.Visual.SourceWidth, state.Visual.SourceHeight, _project.Video, 8 / PreviewScale);
        }

        ClipBounds bounds = ClipGeometry.Fit(state.Candidate, state.Visual.SourceWidth, state.Visual.SourceHeight, _project.Video);
        PlacePreviewImage(state.Visual.Image, bounds);
        if (state.Resize && state.Candidate.Kind == "subtitle") RefreshDraggingSubtitle(state);
        UpdateTransformOverlay(bounds);
        e.Handled = true;
    }

    private bool _renderingSubtitleDrag;
    private async void RefreshDraggingSubtitle(PreviewDrag state)
    {
        if (_renderingSubtitleDrag) return;
        _renderingSubtitleDrag = true;
        try
        {
            (double Scale, double X, double Y)? rendered = null;
            while (_previewDrag == state && !_closed)
            {
                await Task.Delay(40);
                if (_previewDrag != state || _closed) break;
                EditClip clip = ClipEditing.Clone(state.Candidate);
                (double Scale, double ScaleX, double ScaleY) scale = (clip.Scale, clip.ScaleX, clip.ScaleY);
                if (rendered == scale) break;
                VideoSettings video = _project.Copy().Video;
                string path = await Task.Run(() => SubtitleRenderer.SourcePath(clip, video));
                if (_previewDrag != state || _closed) break;
                if ((state.Candidate.Scale, state.Candidate.ScaleX, state.Candidate.ScaleY) != scale) continue;
                using Windows.Storage.Streams.IRandomAccessStreamWithContentType stream = await (await Windows.Storage.StorageFile.GetFileFromPathAsync(path)).OpenReadAsync();
                Microsoft.UI.Xaml.Media.Imaging.BitmapImage bitmap = new();
                await bitmap.SetSourceAsync(stream);
                if (_previewDrag != state || _closed) break;
                state.Visual.Image.Source = bitmap;
                rendered = scale;
            }
        }
        catch (Exception exception)
        {
            if (!_closed && _previewDrag == state) Status.Text = exception.Message;
        }
        finally { _renderingSubtitleDrag = false; }
    }
    private async void Preview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        PreviewDrag? state = _previewDrag;
        if (state == null || state.PointerId != e.Pointer.PointerId)
            return;
        _previewDrag = null;
        SetPreviewCursor(0, 0);
        PreviewInteraction.ReleasePointerCaptures();
        e.Handled = true;
        try
        {
            bool changed = state.Candidate.OffsetX != state.Original.OffsetX || state.Candidate.OffsetY != state.Original.OffsetY || state.Candidate.Scale != state.Original.Scale || state.Candidate.ScaleX != state.Original.ScaleX || state.Candidate.ScaleY != state.Original.ScaleY;
            if (changed)
            {
                string previous = ProjectSnapshot();
                EditProject next = _project.Copy();
                next.Clips[next.Clips.FindIndex(clip => clip.Id == state.Original.Id)] = state.Candidate;
                await CompositionBuilder.Build(next);
                Remember();
                _project = next;
                await Refresh();
                await AutoSaveChanges(previous);
            }

            Select(_project.Clips.Single(clip => clip.Id == state.Original.Id));
        }
        catch (Exception exception)
        {
            PlacePreviewImage(state.Visual.Image, state.OriginalBounds);
            UpdateTransformOverlay();
            Status.Text = exception.Message;
        }
        finally
        {
            _gate.Release();
            RequestStill();
        }
    }

    private void Preview_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        PreviewDrag? state = _previewDrag;
        if (state == null || state.PointerId != e.Pointer.PointerId)
            return;
        _previewDrag = null;
        SetPreviewCursor(0, 0);
        PreviewInteraction.ReleasePointerCaptures();
        PlacePreviewImage(state.Visual.Image, state.OriginalBounds);
        UpdateTransformOverlay();
        _gate.Release();
        RequestStill();
    }
}



