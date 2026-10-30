using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private async void SeekPosition_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var seconds = SubtitleSettingsEditor.Number(playhead, 0, ProjectEnd);
        seconds.Width = 240;
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 16 };
        content.Children.Add(SubtitleSettingsEditor.Group("移動先", ("秒数", seconds)));
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "指定した秒数へ移動", Content = content,
            PrimaryButtonText = "移動", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary
        };
        double position = playhead;
        dialog.PrimaryButtonClick += (_, args) =>
        {
            try { position = ReadDialogNumber(seconds, "秒数"); }
            catch (Exception exception) { args.Cancel = true; error.Text = exception.Message; }
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Pause();
            Seek(position);
        }
    });

    private async void VideoSettings_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var width = SubtitleSettingsEditor.Number(project.Video.Width, 16, 7680);
        var height = SubtitleSettingsEditor.Number(project.Video.Height, 16, 4320);
        var rates = new[] { 24, 25, 30, 50, 60 };
        var fps = SubtitleSettingsEditor.Choice(rates.Select(rate => $"{rate} fps").ToArray(), Array.IndexOf(rates, project.Video.FrameRate));
        var audio = SubtitleSettingsEditor.Choice(new[] { "44.1 kHz", "48 kHz" }, project.Video.AudioSampleRate == 48000 ? 1 : 0);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var content = new StackPanel { Spacing = 24, MinWidth = 440 };
        content.Children.Add(SubtitleSettingsEditor.Group("動画", ("幅（px）", width), ("高さ（px）", height), ("フレームレート", fps)));
        content.Children.Add(SubtitleSettingsEditor.Group("音声", ("サンプルレート", audio)));
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "動画設定",
            Content = new ScrollViewer { Content = content, MaxHeight = Math.Max(200, Math.Min(520, AppHost.ActualHeight - 220)) },
            PrimaryButtonText = "適用", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary
        };
        EditProject? candidate = null;
        Windows.Media.Editing.MediaComposition? prepared = null;
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                var w = ReadDialogNumber(width, "幅");
                var h = ReadDialogNumber(height, "高さ");
                if (w != Math.Truncate(w) || h != Math.Truncate(h)) throw new ArgumentException("解像度は整数で入力してください。");
                var next = project.Copy();
                next.Video = new VideoSettings
                {
                    Width = (int)w, Height = (int)h, FrameRate = rates[fps.SelectedIndex],
                    AudioSampleRate = audio.SelectedIndex == 0 ? 44100 : 48000
                };
                prepared = await CompositionBuilder.Build(next);
                candidate = next;
            }
            catch (Exception exception) { args.Cancel = true; error.Text = exception.Message; }
            finally { deferral.Complete(); }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || candidate == null || prepared == null) return;
        var previous = project;
        project = candidate;
        try { await Refresh(prepared); }
        catch { project = previous; throw; }
        undo.Push(previous.Copy());
        Draw();
        Status.Text = "動画設定を反映しました。";
    });

    private static double ReadDialogNumber(NumberBox input, string label)
    {
        if (!double.TryParse(input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out var value) ||
            !double.IsFinite(value) || value < input.Minimum || value > input.Maximum)
            throw new ArgumentException($"{label}の値が不正です。");
        return value;
    }

    private void Timeline_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (gate.CurrentCount == 0) return;
        for (var node = e.OriginalSource as DependencyObject; node != null && node != Timeline;
             node = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(node))
            if (node is TimelineClipView) return;
        var point = e.GetPosition(Timeline);
        var time = ClipEditing.Snap(Math.Max(0, point.X - TrackLeft) / PixelsPerSecond);
        var layer = Math.Clamp((int)((point.Y - RulerHeight) / TrackHeight) + 1, 1, Math.Min(2000, project.LayerCount));
        var menu = new MenuFlyout();
        menu.Items.Add(CreateAddSubtitleItem(time, layer));
        menu.ShowAt(Timeline, point);
        e.Handled = true;
    }

    private MenuFlyoutItem CreateAddSubtitleItem(double? time = null, int? layer = null)
    {
        var add = new MenuFlyoutItem { Text = "字幕の追加" };
        add.Click += async (_, _) => await Run(() => EditSubtitle(null, time, layer));
        return add;
    }
}
