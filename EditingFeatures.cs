using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private readonly Dictionary<string, PcmAudio> analyzedAudio = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> failedAudio = new(StringComparer.OrdinalIgnoreCase);

    private async Task ApplyTimelineProject(EditProject next)
    {
        var prepared = await CompositionBuilder.Build(next);
        Remember();
        project = next;
        selectedIds.RemoveWhere(id => !project.Clips.Any(c => c.Id == id));
        UpdatePrimarySelection();
        Draw();
        await Refresh(prepared);
    }

    private async Task DeleteSelection(bool ripple)
    {
        var ids = selectedIds.ToHashSet();
        if (ids.Count == 0 && selected != null) ids.Add(selected);
        if (ids.Count == 0) return;
        await ApplyTimelineProject(TimelineOperations.Delete(project, ids, ripple));
    }
    private async void RippleDelete_Click(object sender, RoutedEventArgs e) => await Run(() => DeleteSelection(true));
    private async void CloseGaps_Click(object sender, RoutedEventArgs e) => await Run(() => ApplyTimelineProject(TimelineOperations.CloseGaps(project)));

    private FrameworkElement LayerHeader(int number)
    {
        var settings = project.Layer(number);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Height = 40 };
        row.Children.Add(new TextBlock { Text = $"{number}", Width = 28, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center });
        void Add(string glyph, string name, bool active, Action<LayerSettings, bool> set)
        {
            var button = new Microsoft.UI.Xaml.Controls.Primitives.ToggleButton
            {
                Content = new FontIcon { Glyph = glyph, FontSize = 14 }, IsChecked = active,
                Width = 32, Height = 32, Padding = new Thickness(4), VerticalAlignment = VerticalAlignment.Center
            };
            ToolTipService.SetToolTip(button, $"レイヤー {number}: {name}");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, $"レイヤー {number}: {name}");
            button.Click += async (_, _) => await Run(async () =>
            {
                var next = project.Copy();
                if (!next.Layers.TryGetValue(number, out var state)) next.Layers[number] = state = new();
                set(state, button.IsChecked == true);
                try { await ApplyTimelineProject(next); }
                catch { Draw(); throw; }
            });
            row.Children.Add(button);
        }
        Add("\uE72E", "ロック", settings.Locked, (state, value) => state.Locked = value);
        Add("\uED1A", "非表示（映像・音声）", settings.Hidden, (state, value) => state.Hidden = value);
        Add("\uE74F", "ミュート", settings.Muted, (state, value) => state.Muted = value);
        return row;
    }

    private async void AddWaveform(Grid view, EditClip clip)
    {
        if (clip.Kind is not ("audio" or "video") || failedAudio.Contains(clip.Path)) return;
        var shape = new Polyline
        {
            Stroke = new SolidColorBrush(Microsoft.UI.Colors.White), StrokeThickness = 1,
            Opacity = .65, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2)
        };
        view.Children.Insert(0, shape);
        try
        {
            var audio = await AudioAnalysis.Get(clip.Path);
            if (closed) return;
            analyzedAudio[clip.Path] = audio;
            double width = Math.Max(16, clip.Duration * PixelsPerSecond);
            int count = Math.Clamp((int)Math.Ceiling(width / 2), 2, 2048);
            var points = new PointCollection();
            for (int i = 0; i < count; i++)
            {
                double local = clip.Duration * i / count;
                double amplitude = audio.Peak(clip.Trim + local, clip.Trim + clip.Duration * (i + 1) / count) * clip.Volume * clip.AudioGain(local);
                double x = width * i / count;
                points.Add(new Point(x, 7 - amplitude * 7));
                points.Add(new Point(x, 7 + amplitude * 7));
            }
            shape.Points = points;
        }
        catch (Exception)
        {
            failedAudio.Add(clip.Path);
            if (!closed && clip.Kind == "audio") ToolTipService.SetToolTip(view, $"{clip}\n音声波形を解析できませんでした。素材を確認してください。");
        }
    }

    private void UpdateAudioMeter()
    {
        if (AudioMeter == null) return;
        bool playing = Preview.MediaPlayer.PlaybackSession.PlaybackState == Windows.Media.Playback.MediaPlaybackState.Playing;
        double peak = 0;
        bool pending = false;
        foreach (var clip in project.Clips.Where(c => c.Kind is "audio" or "video" && !project.Layer(c.Layer).Muted && !project.Layer(c.Layer).Hidden && playhead >= c.Start && playhead < c.Start + c.Duration))
        {
            if (!analyzedAudio.TryGetValue(clip.Path, out var audio)) { pending |= !failedAudio.Contains(clip.Path); continue; }
            double local = playhead - clip.Start;
            peak += audio.Peak(clip.Trim + local, clip.Trim + Math.Min(clip.Duration, local + .033)) * clip.Volume * clip.AudioGain(local);
        }
        if (!playing) peak = 0;
        double db = peak > 0 ? 20 * Math.Log10(peak) : -60;
        AudioMeter.Value = Math.Clamp(db, -60, 0);
        AudioMeter.Foreground = new SolidColorBrush(peak >= 1 ? Microsoft.UI.Colors.Red : Microsoft.UI.Colors.LimeGreen);
        AudioMeterText.Text = pending ? "音声を解析中…" : peak <= 0 ? "−∞ dBFS" : $"{db:F1} dBFS（推定）{(peak >= 1 ? " / 音割れの可能性" : "")}";
    }
}
