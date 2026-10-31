using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Xaml.Controls.Primitives;
using Windows.Foundation;
using Windows.Media.Editing;
using Windows.Media.Playback;
using Windows.Storage;
using Windows.UI;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private double PixelsPerSecond = 48; // DIPs, never physical pixels.
    private readonly Canvas timelineRuler = new() { IsHitTestVisible = false };
    private const double TrackLeft = 152;
    private const double RulerHeight = 32;
    private const double TrackHeight = 40;
    private readonly DispatcherTimer playbackTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(33)
    };
    private Border? playheadLine;
    private double playhead, pendingSeek;
    private bool settingSlider, previewOpening, resumePlayback;
    private string? playbackError;
    private bool playbackIntent;
    private uint? scrubPointer;
    private bool resumeAfterScrub;
    private double ProjectEnd => project.Clips.Select(c => c.Start + c.Duration).DefaultIfEmpty(0).Max();

    private DragState? drag;
    private sealed class DragState
    {
        public required Grid View;
        public required EditClip Original;
        public required EditClip Candidate;
        public required Point Origin;
        public required double SourceDuration;
        public int Mode; // -1 left edge, 0 move, 1 right edge
        public bool Changed;
        public List<EditClip> GroupOriginals = [];
        public List<EditClip> GroupCandidates = [];
    }

    private void InitializePlayback()
    {
        Timeline.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Timeline_PointerWheelChanged), true);
        TimelineScroll.AddHandler(UIElement.PointerWheelChangedEvent, new PointerEventHandler(Timeline_PointerWheelChanged), true);
        TimelineScroll.ViewChanged += (_, _) => DrawTimelineRuler();
        var player = new MediaPlayer
        {
            AutoPlay = false
        };
        player.CommandManager.IsEnabled = false;
        Preview.SetMediaPlayer(player);
        player.MediaOpened += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed)
                return;
            previewOpening = false;
            Seek(pendingSeek);
            if (resumePlayback)
                Play();
        });
        player.MediaFailed += (_, e) => DispatcherQueue.TryEnqueue(() =>
        {
            previewOpening = false;
            playbackIntent = false;
            playbackError = e.ErrorMessage;
            resumePlayback = false;
            Status.Text = $"プレビューを再生できません: {e.ErrorMessage}";
        });
        player.MediaEnded += (_, _) => DispatcherQueue.TryEnqueue(() =>
        {
            if (closed)
                return;
            playbackIntent = false;
            resumePlayback = false;
            UpdatePosition(ProjectEnd);
            RequestStill();
        });
        playbackTimer.Tick += (_, _) =>
        {
            UpdateAudioMeter();
            if (scrubPointer == null && !previewOpening && player.Source != null && (player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing || (!stillWorker && PausedPreview.Visibility != Visibility.Visible)))
                UpdatePosition(player.PlaybackSession.Position.TotalSeconds);
            PlayIcon.Glyph = player.PlaybackSession.PlaybackState == MediaPlaybackState.Playing ? "\uE769" : "\uE768";
        };
        playbackTimer.Start();
    }

    private string TimeLabel(double seconds)
    {
        var frames = (long)Math.Round(Math.Max(0, seconds) * project.Video.FrameRate);
        var rate = project.Video.FrameRate;
        return $"{frames / (3600 * rate):00}:{frames / (60 * rate) % 60:00}:{frames / rate % 60:00}.{frames % rate:00}";
    }

    private void UpdatePosition(double seconds)
    {
        playhead = Math.Clamp(seconds, 0, Math.Max(0, ProjectEnd));
        settingSlider = true;
        PositionSlider.Maximum = Math.Max(ClipEditing.MinimumDuration, ProjectEnd);
        PositionSlider.Value = playhead;
        settingSlider = false;
        PositionText.Text = TimeLabel(playhead);
        UpdateStatusBar();
        if (playheadLine != null)
            Canvas.SetLeft(playheadLine, TrackLeft + playhead * PixelsPerSecond - 4);
    }

    private void Seek(double seconds)
    {
        UpdatePosition(seconds);
        pendingSeek = playhead;
        if (!previewOpening && Preview.MediaPlayer.Source != null)
            Preview.MediaPlayer.PlaybackSession.Position = TimeSpan.FromSeconds(playhead);
        RequestStill();
    }

    private void Play()
    {
        if (Preview.MediaPlayer.Source == null)
            return;
        playbackIntent = true;
        if (previewOpening)
            resumePlayback = true;
        else
        {
            if (playhead >= ProjectEnd - 1.0 / project.Video.FrameRate)
                Seek(0);
            HideStill();
            Preview.MediaPlayer.Play();
        }
    }

    private void Pause()
    {
        playbackIntent = false;
        resumePlayback = false;
        Preview.MediaPlayer.Pause();
        RequestStill();
    }

    private void PositionSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (!settingSlider && Preview?.MediaPlayer != null)
            Seek(e.NewValue);
    }

    private void PlayPause_Click(object sender, RoutedEventArgs e)
    {
        TogglePlayback();
    }

    private void TogglePlayback()
    {
        if (Preview.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing || (previewOpening && resumePlayback))
            Pause();
        else
            Play();
    }

    private async void Root_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (ControlPressed && e.Key is Windows.System.VirtualKey.Z or Windows.System.VirtualKey.Y)
        {
            var historyFocus = FocusManager.GetFocusedElement(Root.XamlRoot) as DependencyObject;
            for (var current = historyFocus; current != null; current = VisualTreeHelper.GetParent(current))
                if (current is TextBox or RichEditBox or PasswordBox or NumberBox)
                    return;
            e.Handled = true;
            if (!e.KeyStatus.WasKeyDown)
                await Run(() => e.Key == Windows.System.VirtualKey.Z ? RestoreHistory(undo, redo) : RestoreHistory(redo, undo));
            return;
        }
        if (!IsTransportKey(e.Key))
            return;
        // Consume transport keys before focused controls can edit or activate.
        e.Handled = true;
        if (e.Key is Windows.System.VirtualKey.Left or Windows.System.VirtualKey.Up)
            StepFrame(-1);
        else if (e.Key is Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Down)
            StepFrame(1);
        else if (!e.KeyStatus.WasKeyDown)
            TogglePlayback();
    }

    private static bool IsTransportKey(Windows.System.VirtualKey key) =>
        key is Windows.System.VirtualKey.Space or Windows.System.VirtualKey.Left or
            Windows.System.VirtualKey.Right or Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down;

    private void Root_PreviewKeyUp(object sender, KeyRoutedEventArgs e)
    {
        // Buttons can activate on Space release, so consume both halves of the key press.
        if (IsTransportKey(e.Key))
            e.Handled = true;
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        Pause();
        Seek(0);
    }

    private void Start_Click(object sender, RoutedEventArgs e) => Seek(0);
    private void StepFrame(int direction)
    {
        Pause();
        Seek(ClipEditing.Snap(playhead) + direction / (double)project.Video.FrameRate);
    }

    private void PreviousFrame_Click(object sender, RoutedEventArgs e) => StepFrame(-1);
    private void NextFrame_Click(object sender, RoutedEventArgs e) => StepFrame(1);
    private void Timeline_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (e.Handled || !ControlPressed)
            return;
        var point = e.GetCurrentPoint(TimelineScroll);
        if (point.Properties.IsHorizontalMouseWheel)
            return;
        e.Handled = true;
        if (drag != null || marquee != null || scrubPointer != null || gate.CurrentCount == 0)
            return;

        double next = Math.Clamp(PixelsPerSecond * Math.Pow(1.2, point.Properties.MouseWheelDelta / 120.0), 3, 1536);
        if (next == PixelsPerSecond)
            return;
        // Keep the time beneath the pointer in the same place while zooming.
        double time = Math.Max(0, (TimelineScroll.HorizontalOffset + point.Position.X - TrackLeft) / PixelsPerSecond);
        PixelsPerSecond = next;
        DrawTimeline();
        TimelineScroll.UpdateLayout();
        TimelineScroll.ChangeView(Math.Max(0, TrackLeft + time * PixelsPerSecond - point.Position.X), null, null, true);
        DrawTimelineRuler();
    }

    private void DrawTimelineRuler()
    {
        timelineRuler.Children.Clear();
        double desired = 120 / PixelsPerSecond;
        double magnitude = Math.Pow(10, Math.Floor(Math.Log10(desired)));
        double major = new[] { 1.0, 2, 5, 10 }.First(value => value * magnitude >= desired) * magnitude;
        double step = major / 5;
        double left = Math.Max(0, (TimelineScroll.HorizontalOffset - TrackLeft) / PixelsPerSecond);
        double right = Math.Max(left, (TimelineScroll.HorizontalOffset + TimelineScroll.ViewportWidth - TrackLeft) / PixelsPerSecond);
        long first = Math.Max(0, (long)Math.Floor(left / step) - 5);
        long last = (long)Math.Ceiling(right / step);
        for (long index = first; index <= last; index++)
        {
            bool labeled = index % 5 == 0;
            double x = TrackLeft + index * step * PixelsPerSecond;
            var tick = new Rectangle
            {
                Width = 1, Height = labeled ? 16 : 8,
                Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
            };
            Canvas.SetLeft(tick, x);
            Canvas.SetTop(tick, labeled ? 16 : 24);
            timelineRuler.Children.Add(tick);
            if (!labeled) continue;
            var label = new TextBlock { Text = TimeLabel(index * step), FontSize = 12 };
            Canvas.SetLeft(label, x + 8);
            timelineRuler.Children.Add(label);
        }
    }

    private void TimelineScroll_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!closed && drag == null && marquee == null && scrubPointer == null)
                DrawTimeline();
        });
    }

    private void DrawTimeline()
    {
        displayingObjectValues = true;
        try
        {
            LayerInput.Maximum = Math.Min(2000, project.LayerCount);
            DurationInput.Minimum = 1.0 / project.Video.FrameRate;
            PositionSlider.StepFrequency = 1.0 / project.Video.FrameRate;
        }
        finally { displayingObjectValues = false; }
        Timeline.Children.Clear();
        Timeline.Width = Math.Max((TimelineScroll.ViewportWidth > 0 ? TimelineScroll.ViewportWidth : TimelineScroll.ActualWidth), TrackLeft + (ProjectEnd + 8) * PixelsPerSecond);
        Timeline.Height = RulerHeight + project.LayerCount * TrackHeight;
        for (int i = 0; i < project.LayerCount; i++)
        {
            var label = LayerHeader(i + 1);
            Canvas.SetTop(label, RulerHeight + i * TrackHeight);
            Timeline.Children.Add(label);
            var line = new Rectangle
            {
                Width = Timeline.Width,
                Height = 1,
                Opacity = .25,
                Fill = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
                IsHitTestVisible = false
            };
            Canvas.SetTop(line, RulerHeight + (i + 1) * TrackHeight);
            Timeline.Children.Add(line);
        }

        Timeline.Children.Add(timelineRuler);
        DrawTimelineRuler();

        foreach (var clip in project.Clips)
        {
            var view = new TimelineClipView
            {
                Height = 32,
                Background = ObjectAppearance.Brush(clip),
                Tag = clip.Id
            };
            view.Opacity = project.Layer(clip.Layer).Hidden ? .35 : project.Layer(clip.Layer).Locked ? .65 : 1;
            AddWaveform(view, clip);
            view.Children.Add(new TextBlock {
                Text = (clip.Kind == "audio" ? "♪ " : clip.Kind == "subtitle" ? "字幕: " : "") + clip.ToString(),
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                Margin = new Thickness(8, 0, 8, 0),
                VerticalAlignment = clip.Kind is "audio" or "video" ? VerticalAlignment.Top : VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                IsHitTestVisible = false
            });
            // Explicit handles remain visible even on narrow clips.
            foreach (var alignment in new[]
            {
                HorizontalAlignment.Left,
                HorizontalAlignment.Right
            }

            )
                view.Children.Add(new Border {
                    Width = 8,
                    HorizontalAlignment = alignment,
                    Background = new SolidColorBrush(Color.FromArgb(64, 255, 255, 255)),
                    IsHitTestVisible = false
                });
            view.Children.Add(new Border {
                Tag = "selection",
                Visibility = selectedIds.Contains(clip.Id) ? Visibility.Visible : Visibility.Collapsed,
                Margin = new Thickness(-3),
                Child = new Rectangle { Stroke = ObjectAppearance.Brush(clip), StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false },
                IsHitTestVisible = false
            });
            Place(view, clip);
            ToolTipService.SetToolTip(view, $"{clip} / {clip.Start:F2}s / {clip.Duration:F2}s\n中央: 移動 / 両端: 長さ変更");
            view.PointerPressed += Clip_PointerPressed;
            view.PointerMoved += Clip_PointerMoved;
            view.PointerReleased += Clip_PointerReleased;
            view.PointerCanceled += Clip_PointerCanceled;
            view.PointerCaptureLost += Clip_PointerCanceled;
            view.ContextFlyout = CreateClipContextMenu(clip);
            Timeline.Children.Add(view);
        }

        // Keep the visible line thin, with an 8-DIP hit area for grabbing over clips.
        playheadLine = new Border
        {
            Width = 8,
            Height = Timeline.Height,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Child = new Border
            {
                Width = 2,
                HorizontalAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(Microsoft.UI.Colors.Red)
            }
        };
        Timeline.Children.Add(playheadLine);
        UpdatePosition(playhead);
    }

    private void Place(Grid view, EditClip c)
    {
        view.Width = Math.Max(16, c.Duration * PixelsPerSecond);
        Canvas.SetLeft(view, TrackLeft + c.Start * PixelsPerSecond);
        Canvas.SetTop(view, RulerHeight + (c.Layer - 1) * TrackHeight + 8);
    }

    private void Timeline_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(Timeline);
        if (scrubPointer != null || drag != null || gate.CurrentCount == 0 || !point.Properties.IsLeftButtonPressed || point.Position.X < TrackLeft - 4)
            return;
        if (BeginMarquee(e))
            return;
        if (ControlPressed)
            return;
        if (!Timeline.CapturePointer(e.Pointer))
            return;
        scrubPointer = e.Pointer.PointerId;
        resumeAfterScrub = Preview.MediaPlayer.PlaybackSession.PlaybackState == MediaPlaybackState.Playing || (previewOpening && resumePlayback);
        Pause();
        ScrubTo(point.Position.X);
        e.Handled = true;
    }

    private void ScrubTo(double x) => Seek(ClipEditing.Snap(Math.Max(0, x - TrackLeft) / PixelsPerSecond));
    private void Timeline_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (marquee?.PointerId == e.Pointer.PointerId)
        {
            UpdateMarquee(e);
            return;
        }

        if (scrubPointer != e.Pointer.PointerId)
            return;
        ScrubTo(e.GetCurrentPoint(Timeline).Position.X);
        e.Handled = true;
    }

    private void Timeline_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (marquee?.PointerId == e.Pointer.PointerId)
        {
            UpdateMarquee(e);
            EndMarquee(false);
            return;
        }

        if (scrubPointer != e.Pointer.PointerId)
            return;
        ScrubTo(e.GetCurrentPoint(Timeline).Position.X);
        EndScrub();
        e.Handled = true;
    }

    private void Timeline_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (marquee?.PointerId == e.Pointer.PointerId)
        {
            EndMarquee(true);
            return;
        }

        if (scrubPointer == e.Pointer.PointerId)
            EndScrub();
    }

    private void EndScrub()
    {
        scrubPointer = null;
        Timeline.ReleasePointerCaptures();
        if (resumeAfterScrub)
            Play();
        resumeAfterScrub = false;
    }

    private async void Clip_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (scrubPointer != null || drag != null || marquee != null || !e.GetCurrentPoint((Grid)sender).Properties.IsLeftButtonPressed)
            return;
        if (ControlPressed)
        {
            BeginMarquee(e, (string)((Grid)sender).Tag);
            e.Handled = true;
            return;
        }

        if (!gate.Wait(0))
            return;
        var view = (Grid)sender;
        DragState? started = null;
        try
        {
            var clip = project.Clips.Single(c => c.Id == (string)view.Tag);
            project.RequireEditable(clip.Layer);
            project.RequireEditable(project.Clips.Where(c => selectedIds.Contains(c.Id)).Select(c => c.Layer).ToArray());
            var local = e.GetCurrentPoint(view).Position.X;
            var state = new DragState
            {
                View = view,
                Original = ClipEditing.Clone(clip),
                Candidate = ClipEditing.Clone(clip),
                Origin = e.GetCurrentPoint(Timeline).Position,
                SourceDuration = double.PositiveInfinity,
                Mode = local < 8 ? -1 : local > view.ActualWidth - 8 ? 1 : 0
            };
            started = state;
            bool preserveGroup = selectedIds.Contains(clip.Id);
            Select(clip, false, preserveSelection: preserveGroup);
            if (state.Mode == 0)
            {
                state.GroupOriginals = project.Clips.Where(item => selectedIds.Contains(item.Id)).Select(ClipEditing.Clone).ToList();
                state.GroupCandidates = state.GroupOriginals.Select(ClipEditing.Clone).ToList();
            }

            UpdateSelectionBorders();
            Pause();
            drag = state;
            ((TimelineClipView)view).SetResizeActive(state.Mode != 0);
            if (!view.CapturePointer(e.Pointer))
            {
                drag = null;
                ((TimelineClipView)view).SetResizeActive(false);
                gate.Release();
                return;
            }

            e.Handled = true;
            if (!clip.IsStill && state.Mode != 0)
            {
                var file = await StorageFile.GetFileFromPathAsync(clip.Path);
                state.SourceDuration = clip.Kind == "audio" ? (await BackgroundAudioTrack.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds : (await MediaClip.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds;
            }
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
            if (started == null)
                gate.Release();
            else if (ReferenceEquals(drag, started))
            {
                drag = null;
                ((TimelineClipView)view).SetResizeActive(false);
                view.ReleasePointerCaptures();
                gate.Release();
                Draw();
            }
        }
    }

    private void Clip_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        var state = drag;
        if (state == null || !ReferenceEquals(state.View, sender))
            return;
        var point = e.GetCurrentPoint(Timeline).Position;
        double delta = (point.X - state.Origin.X) / PixelsPerSecond;
        if (state.Mode == 0 && state.GroupOriginals.Count > 1)
        {
            state.GroupCandidates = ClipEditing.MoveGroup(state.GroupOriginals, delta, (int)Math.Round((point.Y - state.Origin.Y) / TrackHeight));
            state.Candidate = state.GroupCandidates.Single(clip => clip.Id == state.Original.Id);
            foreach (var candidate in state.GroupCandidates)
            {
                var groupView = Timeline.Children.OfType<TimelineClipView>().Single(view => (string)view.Tag == candidate.Id);
                Place(groupView, candidate);
            }
        }
        else
        {
            if (state.Mode == 0)
            {
                int layerDelta = (int)Math.Round((point.Y - state.Origin.Y) / TrackHeight);
                state.Candidate = ClipEditing.Move(state.Original, delta, layerDelta);
            }
            else
            {
                state.Candidate = ClipEditing.Resize(state.Original, delta, state.Mode == -1, state.SourceDuration);
            }
            state.Candidate = ClipEditing.SnapToEdges(state.Candidate, project.Clips.Where(clip => !selectedIds.Contains(clip.Id)), state.Mode, 8 / PixelsPerSecond, state.SourceDuration);
            state.GroupCandidates = [state.Candidate];
        }

        state.Changed = state.Candidate.Start != state.Original.Start || state.Candidate.Duration != state.Original.Duration || state.Candidate.Layer != state.Original.Layer;
        Place(state.View, state.Candidate);
        Select(state.Candidate, false, preserveSelection: true);
        e.Handled = true;
    }

    private async void Clip_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        var state = drag;
        if (state == null || !ReferenceEquals(state.View, sender))
            return;
        drag = null;
        ((TimelineClipView)state.View).SetResizeActive(false);
        state.View.ReleasePointerCaptures();
        e.Handled = true;
        try
        {
            if (state.Changed)
            {
                var previous = ProjectSnapshot();
                await CommitTimelineCandidates(state.GroupCandidates);
                await AutoSaveChanges(previous);
            }
        }
        catch (Exception ex)
        {
            Status.Text = ex.Message;
        }
        finally
        {
            Select(project.Clips.Single(c => c.Id == state.Original.Id), preserveSelection: true);
            gate.Release();
        }
    }

    private void Clip_PointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (drag == null || !ReferenceEquals(drag.View, sender))
            return;
        var state = drag;
        drag = null;
        ((TimelineClipView)state.View).SetResizeActive(false);
        state.View.ReleasePointerCaptures();
        gate.Release();
        Select(state.Original, preserveSelection: true);
    }

    private async Task<object> Split(string id, double seconds)
    {
        var next = project.Copy();
        var index = next.Clips.FindIndex(c => c.Id == id);
        if (index < 0)
            throw new ArgumentException("クリップを選択してください。");
        project.RequireEditable(next.Clips[index].Layer);
        var parts = ClipEditing.Split(next.Clips[index], seconds);
        next.Clips[index] = parts.Left;
        next.Clips.Insert(index + 1, parts.Right);
        await CompositionBuilder.Build(next);
        Remember();
        project = next;
        Select(parts.Right);
        await Refresh();
        return new
        {
            left = parts.Left,
            right = parts.Right
        };
    }

    private async void Split_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (selected == null)
            throw new ArgumentException("分割するクリップを選択してください。");
        await Split(selected, playhead);
    });
}







