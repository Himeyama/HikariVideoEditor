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
    private readonly HashSet<string> selectedIds = [];
    private MarqueeSelection? marquee;
    private sealed record MarqueeSelection(uint PointerId, Point Origin, HashSet<string> Before, Border Box, string? ClickedId)
    {
        public bool Dragged { get; set; }
    }
    private static bool ControlPressed => (InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control) & CoreVirtualKeyStates.Down) != 0;

    private void UpdatePrimarySelection()
    {
        var primary = project.Clips.FirstOrDefault(clip => selectedIds.Contains(clip.Id));
        if (primary == null)
        {
            selected = null;
            SelectedName.Text = "クリップ未選択";
        }
        else
        {
            Select(primary, redraw: false, preserveSelection: true);
        }
    }

    private bool BeginMarquee(PointerRoutedEventArgs e, string? clickedId = null)
    {
        if (!ControlPressed || marquee != null || !gate.Wait(0))
            return false;
        if (!Timeline.CapturePointer(e.Pointer))
        {
            gate.Release();
            return false;
        }

        var box = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            Opacity = 0.35,
            IsHitTestVisible = false
        };
        marquee = new MarqueeSelection(e.Pointer.PointerId, e.GetCurrentPoint(Timeline).Position, new HashSet<string>(selectedIds), box, clickedId);
        Timeline.Children.Add(box);
        e.Handled = true;
        return true;
    }

    private void UpdateMarquee(PointerRoutedEventArgs e)
    {
        var selection = marquee!;
        var point = e.GetCurrentPoint(Timeline).Position;
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
        selectedIds.Clear();
        selectedIds.UnionWith(selection.Before);
        foreach (var clip in project.Clips)
        {
            double x = TrackLeft + clip.Start * PixelsPerSecond;
            double y = RulerHeight + (clip.Layer - 1) * TrackHeight + 8;
            if (x < left + width && x + Math.Max(16, clip.Duration * PixelsPerSecond) > left && y < top + height && y + 32 > top)
                selectedIds.Add(clip.Id);
        }

        UpdatePrimarySelection();
        UpdateSelectionBorders();
        e.Handled = true;
    }

    private void EndMarquee(bool cancel)
    {
        var selection = marquee!;
        marquee = null;
        Timeline.ReleasePointerCaptures();
        if (cancel)
        {
            selectedIds.Clear();
            selectedIds.UnionWith(selection.Before);
        }

        if (!cancel && !selection.Dragged && selection.ClickedId != null)
        {
            selectedIds.Clear();
            selectedIds.UnionWith(selection.Before);
            if (!selectedIds.Add(selection.ClickedId)) selectedIds.Remove(selection.ClickedId);
        }
        Timeline.Children.Remove(selection.Box);
        gate.Release();
        UpdatePrimarySelection();
        Draw();
    }

    private void UpdateSelectionBorders()
    {
        foreach (var view in Timeline.Children.OfType<TimelineClipView>())
            foreach (var border in view.Children.OfType<Border>().Where(border => Equals(border.Tag, "selection")))
                border.Visibility = selectedIds.Contains((string)view.Tag) ? Visibility.Visible : Visibility.Collapsed;
    }

    private async Task CommitTimelineCandidates(IEnumerable<EditClip> candidates)
    {
        var next = project.Copy();
        foreach (var candidate in candidates)
            next.Clips[next.Clips.FindIndex(clip => clip.Id == candidate.Id)] = candidate;
        await CompositionBuilder.Build(next);
        Remember();
        project = next;
        await Refresh();
    }

    private async Task MoveSelection(double seconds, int layers)
    {
        var originals = project.Clips.Where(clip => selectedIds.Contains(clip.Id)).ToList();
        if (originals.Count == 0)
            throw new ArgumentException("移動する素材を選択してください。");
        if (!double.IsFinite(seconds))
            throw new ArgumentException("移動時間が不正です。");
        await CommitTimelineCandidates(ClipEditing.MoveGroup(originals, seconds, layers));
        UpdatePrimarySelection();
        Draw();
    }
}
