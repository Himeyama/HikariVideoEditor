using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private void UpdateGenerationAvailability()
    {
        bool enabled = !string.IsNullOrWhiteSpace(project.WorkspaceDirectory);
        CreateSpeechMenuItem.IsEnabled = enabled;
        CreateImageMenuItem.IsEnabled = enabled;
    }

    private void RequireGenerationWorkspace()
    {
        if (string.IsNullOrWhiteSpace(project.WorkspaceDirectory))
            throw new InvalidOperationException("音声・画像を作成するにはワークスペースを開いてください。");
    }

    private string GeneratedPath(string content, string extension)
    {
        RequireGenerationWorkspace();
        var directory = Path.Combine(project.WorkspaceDirectory!, "Generated");
        Directory.CreateDirectory(directory);
        return MediaGeneration.ContentPath(directory, content, extension);
    }

    private async Task<object> GenerateMedia(JsonElement p, bool speech)
    {
        RequireGenerationWorkspace();
        string? StringArg(string key) => p.TryGetProperty(key, out var v) ? v.GetString() : null;
        double NumberArg(string key, double fallback) => p.TryGetProperty(key, out var v) ? v.GetDouble() : fallback;
        bool add = p.TryGetProperty("addToTimeline", out var a) && a.GetBoolean();
        double start = NumberArg("start", playhead), duration = NumberArg("duration", 5);
        if (!double.IsFinite(start) || start < 0 || start > 86400 || !double.IsFinite(duration) || duration < 1.0 / project.Video.FrameRate || start + duration > 86400)
            throw new ArgumentException("開始時刻または表示時間が不正です。");
        string path = StringArg("path") ?? GeneratedPath(StringArg(speech ? "text" : "title") ?? "", speech ? ".wav" : ".png");
        if (speech && StringArg("voice") is string voiceId && voiceId.StartsWith("voicevox:", StringComparison.Ordinal))
        {
            var favorite = preferences.VoicevoxFavorites.FirstOrDefault(v => v.VoiceId == voiceId)
                ?? throw new ArgumentException("VOICEVOXのお気に入りを登録してください。");
            await Voicevox.Speech(path, StringArg("text") ?? "", favorite,
                p.TryGetProperty("rate", out var vr) ? vr.GetInt32() : 0,
                p.TryGetProperty("volume", out var vv) ? vv.GetInt32() : 100);
        }
        else if (speech)
            await MediaGeneration.Speech(path, StringArg("text") ?? "", StringArg("voice"),
                p.TryGetProperty("rate", out var rate) ? rate.GetInt32() : 0,
                p.TryGetProperty("volume", out var volume) ? volume.GetInt32() : 100);
        else
            await MediaGeneration.Image(path, new MediaGeneration.ImageOptions(StringArg("title") ?? "", StringArg("text") ?? "", StringArg("footer") ?? "",
                p.TryGetProperty("width", out var width) ? width.GetInt32() : project.Video.Width,
                p.TryGetProperty("height", out var height) ? height.GetInt32() : project.Video.Height,
                StringArg("background") ?? "#101B30", StringArg("textColor") ?? "#FFFFFF", StringArg("accent") ?? "#57E3C0",
                StringArg("fontFamily") ?? "Yu Gothic", NumberArg("fontSize", 64)));
        if (speech)
        {
            preferences.LastSpeechVoice = StringArg("voice");
            preferences.LastSpeechRate = p.TryGetProperty("rate", out var savedRate) ? savedRate.GetInt32() : 0;
            preferences.LastSpeechVolume = p.TryGetProperty("volume", out var savedVolume) ? savedVolume.GetInt32() : 100;
            preferences.Save(AppPreferences.DefaultPath);
        }
        EditClip? clip = null;
        if (add)
        {
            if (speech)
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);
                duration = (await Windows.Media.Editing.BackgroundAudioTrack.CreateFromFileAsync(file)).OriginalDuration.TotalSeconds;
            }
            clip = new EditClip { Path = path, Kind = speech ? "audio" : "image", Start = start, Duration = duration,
                Layer = ClipPlacement.FirstAvailableLayer(project.Clips, start, duration) };
            clip.Validate(project.Video.FrameRate);
            project.RequireEditable(clip.Layer);
            Remember();
            project.Clips.Add(clip);
            Select(clip);
        }
        return new { path, clip };
    }

    private async void CreateSpeech_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireGenerationWorkspace();
        var voices = (await MediaGeneration.Voices()).Concat(preferences.VoicevoxFavorites.Select(v => new MediaGeneration.Voice(v.VoiceId, v.Name, "ja-JP"))).ToArray();
        if (voices.Length == 0) throw new InvalidOperationException("音声がありません。Windowsの設定で音声を追加してください。");
        var voice = new ComboBox { Header = "音声", HorizontalAlignment = HorizontalAlignment.Stretch, ItemsSource = voices, DisplayMemberPath = "Name" };
        voice.SelectedItem = voices.FirstOrDefault(v => v.Id == preferences.LastSpeechVoice)
            ?? voices.FirstOrDefault(v => v.Language == "ja-JP") ?? voices[0];
        var text = new TextBox { Header = "読み上げる原稿", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 180, MaxLength = 10000 };
        var rate = new NumberBox { Header = "速度（-10：ゆっくり ～ 10：速い）", Minimum = -10, Maximum = 10, Value = preferences.LastSpeechRate, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var volume = new NumberBox { Header = "音量（0〜100）", Minimum = 0, Maximum = 100, Value = preferences.LastSpeechVolume, SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact };
        var path = new TextBox { Header = "保存先（新しいWAVファイル）", IsReadOnly = true };
        text.TextChanged += (_, _) => path.Text = GeneratedPath(text.Text, ".wav");
        var add = new CheckBox { Content = "再生位置に追加する", IsChecked = true };
        var panel = new StackPanel { Spacing = 12, Width = 480 };
        panel.Children.Add(new TextBlock { Text = "Windowsの音声、またはVOICEVOXのお気に入りから音声を選べます。VOICEVOXはステータスバーから起動・お気に入り登録できます。", TextWrapping = TextWrapping.Wrap });
        foreach (var control in new UIElement[] { text, voice, rate, volume, path, add }) panel.Children.Add(control);
        await GenerationDialog("音声を作成", panel, async () =>
        {
        path.Text = GeneratedPath(text.Text, ".wav");
        if (!double.IsFinite(rate.Value) || rate.Value != Math.Truncate(rate.Value) || !double.IsFinite(volume.Value) || volume.Value != Math.Truncate(volume.Value))
            throw new ArgumentException("速度と音量は整数で入力してください。");
        await GenerateMedia(JsonSerializer.SerializeToElement(new { path = path.Text, text = text.Text, voice = ((MediaGeneration.Voice)voice.SelectedItem).Id, rate = (int)rate.Value, volume = (int)volume.Value, addToTimeline = add.IsChecked == true }), true);
        if (add.IsChecked == true) await Refresh();
        Status.Text = $"音声を保存しました: {path.Text}";
        }).ShowAsync();
    });

    private async void CreateImage_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        RequireGenerationWorkspace();
        var title = new TextBox { Header = "見出し", MaxLength = 200 };
        var text = new TextBox { Header = "本文", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 140, MaxLength = 4000 };
        var footer = new TextBox { Header = "フッター", MaxLength = 300 };
        var background = new TextBox { Header = "背景色（#RRGGBB）", Text = "#101B30" };
        var textColor = new TextBox { Header = "文字色（#RRGGBB）", Text = "#FFFFFF" };
        var accent = new TextBox { Header = "アクセント色（#RRGGBB）", Text = "#57E3C0" };
        var duration = new NumberBox { Header = "表示時間（秒）", Minimum = 1.0 / project.Video.FrameRate, Maximum = 86400, Value = 5 };
        var path = new TextBox { Header = "保存先（新しいPNGファイル）", IsReadOnly = true };
        title.TextChanged += (_, _) => path.Text = GeneratedPath(title.Text, ".png");
        var add = new CheckBox { Content = "再生位置に追加する", IsChecked = true };
        var panel = new StackPanel { Spacing = 12, Width = 480 };
        panel.Children.Add(new TextBlock { Text = $"見出し・本文・図形の画像を作成します（{project.Video.Width} × {project.Video.Height}）。", TextWrapping = TextWrapping.Wrap });
        foreach (var control in new UIElement[] { title, text, footer, background, textColor, accent, duration, path, add }) panel.Children.Add(control);
        await GenerationDialog("画像を作成", panel, async () =>
        {
        path.Text = GeneratedPath(title.Text, ".png");
        await GenerateMedia(JsonSerializer.SerializeToElement(new { path = path.Text, title = title.Text, text = text.Text, footer = footer.Text,
            background = background.Text, textColor = textColor.Text, accent = accent.Text, duration = duration.Value, addToTimeline = add.IsChecked == true }), false);
        if (add.IsChecked == true) await Refresh();
        Status.Text = $"画像を保存しました: {path.Text}";
        }).ShowAsync();
    });

    private ContentDialog GenerationDialog(string title, StackPanel content, Func<Task> create)
    {
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        content.Children.Add(error);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = title,
            Content = new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = Math.Max(200, Math.Min(560, AppHost.ActualHeight - 220)) },
            PrimaryButtonText = "作成", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;
            dialog.CloseButtonText = "";
            content.IsHitTestVisible = false;
            try { error.Text = "作成中…"; await create(); }
            catch (Exception ex) { args.Cancel = true; error.Text = ex.Message; }
            finally
            {
                content.IsHitTestVisible = true;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.CloseButtonText = "キャンセル";
                deferral.Complete();
            }
        };
        return dialog;
    }
}
