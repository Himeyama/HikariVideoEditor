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

    private List<PreviewVisual> previewVisuals = [];
    private PreviewDrag? previewDrag;
    private double PreviewScale => Math.Min(PreviewViewport.ActualWidth / project.Video.Width, PreviewViewport.ActualHeight / project.Video.Height);

    private Point ProjectPoint(Point point)
    {
        double scale = PreviewScale;
        double left = (PreviewViewport.ActualWidth - project.Video.Width * scale) / 2;
        double top = (PreviewViewport.ActualHeight - project.Video.Height * scale) / 2;
        return new Point((point.X - left) / scale, (point.Y - top) / scale);
    }

    private ClipBounds ViewportBounds(ClipBounds bounds)
    {
        double scale = PreviewScale;
        double left = (PreviewViewport.ActualWidth - project.Video.Width * scale) / 2;
        double top = (PreviewViewport.ActualHeight - project.Video.Height * scale) / 2;
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
        var visual = previewVisuals.FirstOrDefault(item => item.Clip.Id == selected);
        if (visual == null)
            return;
        var bounds = ViewportBounds(changingBounds ?? visual.Bounds);
        var stroke = ObjectAppearance.Brush(visual.Clip);
        var outline = new Microsoft.UI.Xaml.Shapes.Rectangle
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
        foreach (var point in new[]
        {
            new Point(bounds.X, bounds.Y), new Point(bounds.Right, bounds.Y),
            new Point(bounds.X, bounds.Bottom), new Point(bounds.Right, bounds.Bottom),
            new Point((bounds.X + bounds.Right) / 2, bounds.Y), new Point((bounds.X + bounds.Right) / 2, bounds.Bottom),
            new Point(bounds.X, (bounds.Y + bounds.Bottom) / 2), new Point(bounds.Right, (bounds.Y + bounds.Bottom) / 2)
        })
        {
            var handle = new Border { Width = 8, Height = 8, Background = stroke, IsHitTestVisible = false };
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
        var shape = horizontal != 0 && vertical != 0
            ? horizontal == vertical ? InputSystemCursorShape.SizeNorthwestSoutheast : InputSystemCursorShape.SizeNortheastSouthwest
            : horizontal != 0 ? InputSystemCursorShape.SizeWestEast
            : vertical != 0 ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.Arrow;
        PreviewInteraction.SetCursor(shape);
    }

    private void Preview_PointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (previewDrag == null) SetPreviewCursor(0, 0);
    }
    private void Preview_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var pointer = e.GetCurrentPoint(PreviewInteraction);
        if (!pointer.Properties.IsLeftButtonPressed || previewDrag != null || drag != null || scrubPointer != null || PreviewScale <= 0)
            return;
        Pause();
        var projectPoint = ProjectPoint(pointer.Position);
        var selectedVisual = previewVisuals.FirstOrDefault(item => item.Clip.Id == selected);
        var edges = selectedVisual == null ? (Horizontal: 0, Vertical: 0)
            : PreviewEdges(pointer.Position, ViewportBounds(selectedVisual.Bounds));
        bool resize = edges.Horizontal != 0 || edges.Vertical != 0;
        var visual = resize ? selectedVisual : previewVisuals.LastOrDefault(item => item.Bounds.Contains(projectPoint.X, projectPoint.Y));
        if (visual == null || !gate.Wait(0))
            return;
        var clip = project.Clips.FirstOrDefault(item => item.Id == visual.Clip.Id && playhead >= item.Start && playhead < item.Start + item.Duration);
        if (clip == null || !PreviewInteraction.CapturePointer(e.Pointer))
        {
            gate.Release();
            return;
        }

        var original = ClipEditing.Clone(clip);
        original.SubtitleBounds = visual.Clip.SubtitleBounds;
        previewDrag = new PreviewDrag(e.Pointer.PointerId, original, visual.Bounds, projectPoint, visual, edges.Horizontal, edges.Vertical);
        SetPreviewCursor(edges.Horizontal, edges.Vertical);
        Select(clip);
        e.Handled = true;
    }

    private void Preview_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var state = previewDrag;
        if (state == null)
        {
            var visual = previewVisuals.FirstOrDefault(item => item.Clip.Id == selected);
            var edges = visual == null ? (Horizontal: 0, Vertical: 0) : PreviewEdges(e.GetCurrentPoint(PreviewInteraction).Position, ViewportBounds(visual.Bounds));
            SetPreviewCursor(edges.Horizontal, edges.Vertical);
            return;
        }
        if (state.PointerId != e.Pointer.PointerId) return;
        var point = ProjectPoint(e.GetCurrentPoint(PreviewInteraction).Position);
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
            state.Candidate = ClipGeometry.SnapToCanvas(state.Candidate, state.Visual.SourceWidth, state.Visual.SourceHeight, project.Video, 8 / PreviewScale);
        }

        var bounds = ClipGeometry.Fit(state.Candidate, state.Visual.SourceWidth, state.Visual.SourceHeight, project.Video);
        PlacePreviewImage(state.Visual.Image, bounds);
        if (state.Resize && state.Candidate.Kind == "subtitle") RefreshDraggingSubtitle(state);
        UpdateTransformOverlay(bounds);
        e.Handled = true;
    }

    private bool renderingSubtitleDrag;
    private async void RefreshDraggingSubtitle(PreviewDrag state)
    {
        if (renderingSubtitleDrag) return;
        renderingSubtitleDrag = true;
        try
        {
            (double Scale, double X, double Y)? rendered = null;
            while (previewDrag == state && !closed)
            {
                await Task.Delay(40);
                if (previewDrag != state || closed) break;
                var clip = ClipEditing.Clone(state.Candidate);
                var scale = (clip.Scale, clip.ScaleX, clip.ScaleY);
                if (rendered == scale) break;
                var video = project.Copy().Video;
                var path = await Task.Run(() => SubtitleRenderer.SourcePath(clip, video));
                if (previewDrag != state || closed) break;
                if ((state.Candidate.Scale, state.Candidate.ScaleX, state.Candidate.ScaleY) != scale) continue;
                using var stream = await (await Windows.Storage.StorageFile.GetFileFromPathAsync(path)).OpenReadAsync();
                var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                await bitmap.SetSourceAsync(stream);
                if (previewDrag != state || closed) break;
                state.Visual.Image.Source = bitmap;
                rendered = scale;
            }
        }
        catch (Exception exception)
        {
            if (!closed && previewDrag == state) Status.Text = exception.Message;
        }
        finally { renderingSubtitleDrag = false; }
    }
    private async void Preview_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var state = previewDrag;
        if (state == null || state.PointerId != e.Pointer.PointerId)
            return;
        previewDrag = null;
        SetPreviewCursor(0, 0);
        PreviewInteraction.ReleasePointerCaptures();
        e.Handled = true;
        try
        {
            bool changed = state.Candidate.OffsetX != state.Original.OffsetX || state.Candidate.OffsetY != state.Original.OffsetY || state.Candidate.Scale != state.Original.Scale || state.Candidate.ScaleX != state.Original.ScaleX || state.Candidate.ScaleY != state.Original.ScaleY;
            if (changed)
            {
                var previous = ProjectSnapshot();
                var next = project.Copy();
                next.Clips[next.Clips.FindIndex(clip => clip.Id == state.Original.Id)] = state.Candidate;
                await CompositionBuilder.Build(next);
                Remember();
                project = next;
                await Refresh();
                await AutoSaveChanges(previous);
            }

            Select(project.Clips.Single(clip => clip.Id == state.Original.Id));
        }
        catch (Exception exception)
        {
            PlacePreviewImage(state.Visual.Image, state.OriginalBounds);
            UpdateTransformOverlay();
            Status.Text = exception.Message;
        }
        finally
        {
            gate.Release();
            RequestStill();
        }
    }

    private void Preview_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        var state = previewDrag;
        if (state == null || state.PointerId != e.Pointer.PointerId)
            return;
        previewDrag = null;
        SetPreviewCursor(0, 0);
        PreviewInteraction.ReleasePointerCaptures();
        PlacePreviewImage(state.Visual.Image, state.OriginalBounds);
        UpdateTransformOverlay();
        gate.Release();
        RequestStill();
    }
}



