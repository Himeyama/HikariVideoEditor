using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI.Core;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private readonly HashSet<string> _selectedIds = [];
    private MarqueeSelection? _marquee;
    private sealed record MarqueeSelection(uint PointerId, Point Origin, HashSet<string> Before, Border Box, string? ClickedId)
    {
        public bool Dragged { get; set; }
    }
    private static bool ControlPressed => (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

    private void UpdatePrimarySelection()
    {
        EditClip? primary = _project.Clips.FirstOrDefault(clip => _selectedIds.Contains(clip.Id));
        if (primary == null)
        {
            _selected = null;
            SelectedName.Text = "クリップ未選択";
        }
        else
        {
            Select(primary, redraw: false, preserveSelection: true);
        }
    }

    private bool BeginMarquee(PointerRoutedEventArgs e, string? clickedId = null)
    {
        if (!ControlPressed || _marquee != null || !_gate.Wait(0))
            return false;
        if (!Timeline.CapturePointer(e.Pointer))
        {
            _gate.Release();
            return false;
        }

        Border box = new()
        {
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Opacity = 0.35,
            IsHitTestVisible = false
        };
        _marquee = new MarqueeSelection(e.Pointer.PointerId, e.GetCurrentPoint(Timeline).Position, new HashSet<string>(_selectedIds), box, clickedId);
        Timeline.Children.Add(box);
        e.Handled = true;
        return true;
    }

    private void UpdateMarquee(PointerRoutedEventArgs e)
    {
        MarqueeSelection selection = _marquee!;
        Point point = e.GetCurrentPoint(Timeline).Position;
        double left = Math.Min(point.X, selection.Origin.X);
        double top = Math.Min(point.Y, selection.Origin.Y);
        double width = Math.Abs(point.X - selection.Origin.X);
        double height = Math.Abs(point.Y - selection.Origin.Y);
        if (!selection.Dragged && width < 4 && height < 4) return;
        selection.Dragged = true;
        Canvas.SetLeft(selection.Box, left);
        Canvas.SetTop(selection.Box, top);
        selection.Box.Width = width;
        selection.Box.Height = height;
        _selectedIds.Clear();
        _selectedIds.UnionWith(selection.Before);
        foreach (EditClip clip in _project.Clips)
        {
            double x = TrackLeft + clip.Start * _pixelsPerSecond;
            double y = RulerHeight + (clip.Layer - 1) * TrackHeight + 8;
            if (x < left + width && x + Math.Max(16, clip.Duration * _pixelsPerSecond) > left && y < top + height && y + 32 > top)
                _selectedIds.Add(clip.Id);
        }

        UpdatePrimarySelection();
        UpdateSelectionBorders();
        e.Handled = true;
    }

    private void EndMarquee(bool cancel)
    {
        MarqueeSelection selection = _marquee!;
        _marquee = null;
        Timeline.ReleasePointerCaptures();
        if (cancel)
        {
            _selectedIds.Clear();
            _selectedIds.UnionWith(selection.Before);
        }

        if (!cancel && !selection.Dragged && selection.ClickedId != null)
        {
            _selectedIds.Clear();
            _selectedIds.UnionWith(selection.Before);
            if (!_selectedIds.Add(selection.ClickedId)) _selectedIds.Remove(selection.ClickedId);
        }
        Timeline.Children.Remove(selection.Box);
        _gate.Release();
        UpdatePrimarySelection();
        Draw();
    }

    private void UpdateSelectionBorders()
    {
        foreach (TimelineClipView view in Timeline.Children.OfType<TimelineClipView>())
            foreach (Border border in view.Children.OfType<Border>().Where(border => Equals(border.Tag, "selection")))
                border.Visibility = _selectedIds.Contains((string)view.Tag) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CommitTimelineCandidates(IEnumerable<EditClip> candidates)
    {
        EditProject next = _project.Copy();
        foreach (EditClip candidate in candidates)
        {
            _project.RequireEditable(_project.Clips.Single(clip => clip.Id == candidate.Id).Layer, candidate.Layer);
            next.Clips[next.Clips.FindIndex(clip => clip.Id == candidate.Id)] = candidate;
        }
        Windows.Media.Editing.MediaComposition prepared = await CompositionBuilder.Build(next);
        Remember();
        _project = next;
        await Refresh(prepared);
    }

    private async Task MoveSelection(double seconds, int layers)
    {
        List<EditClip> originals = _project.Clips.Where(clip => _selectedIds.Contains(clip.Id)).ToList();
        if (originals.Count == 0)
            throw new ArgumentException("移動する素材を選択してください。");
        if (!double.IsFinite(seconds))
            throw new ArgumentException("移動時間が不正です。");
        await CommitTimelineCandidates(ClipEditing.MoveGroup(originals, seconds, layers));
        UpdatePrimarySelection();
        Draw();
    }
}
