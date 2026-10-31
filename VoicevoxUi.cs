using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer voicevoxTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool checkingVoicevox;
    private string? voicevoxExecutable;
    private bool voicevoxRunning;
    private DateTime voicevoxStartingUntil;

    private void InitializeVoicevox()
    {
        voicevoxTimer.Tick += async (_, _) => await UpdateVoicevoxStatus();
        Closed += (_, _) => voicevoxTimer.Stop();
        voicevoxTimer.Start();
        _ = UpdateVoicevoxStatus();
    }

    private async Task UpdateVoicevoxStatus()
    {
        if (checkingVoicevox || closed) return;
        checkingVoicevox = true;
        try
        {
            voicevoxExecutable = await Task.Run(Voicevox.FindExecutable);
            voicevoxRunning = await Voicevox.IsRunning();
            if (closed) return;
            VoicevoxStatusButton.Visibility = voicevoxExecutable != null || voicevoxRunning ? Visibility.Visible : Visibility.Collapsed;
            VoicevoxStatus.Text = voicevoxRunning ? "VOICEVOX" : DateTime.UtcNow < voicevoxStartingUntil ? "VOICEVOX: 起動中…" : "VOICEVOX: 未起動";
            ToolTipService.SetToolTip(VoicevoxStatusButton, voicevoxRunning ? "キャラクターと声のお気に入りを設定" : "クリックしてVOICEVOXを起動");
        }
        catch (Exception ex) { if (!closed) Status.Text = $"VOICEVOXの確認に失敗しました: {ex.Message}"; }
        finally { checkingVoicevox = false; }
    }

    private async void VoicevoxStatus_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await UpdateVoicevoxStatus();
        if (!voicevoxRunning)
        {
            if (DateTime.UtcNow < voicevoxStartingUntil) return;
            if (voicevoxExecutable == null) throw new InvalidOperationException("VOICEVOXが見つかりません。");
            using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(voicevoxExecutable) { UseShellExecute = true });
            voicevoxStartingUntil = DateTime.UtcNow.AddSeconds(60);
            VoicevoxStatus.Text = "VOICEVOX: 起動中…";
            Status.Text = "VOICEVOXを起動しています。準備ができると表示が切り替わります。";
            return;
        }
        var voices = await Voicevox.Voices();
        var panel = new StackPanel { Spacing = 12, Width = 440 };
        panel.Children.Add(new TextBlock { Text = "「音声を作成」で使うキャラクターと声にチェックを入れてください。", TextWrapping = TextWrapping.Wrap });
        var choices = voices.Select(v => (Voice: v, Box: new CheckBox { Content = $"{v.Character}（{v.Style}）", IsChecked = preferences.VoicevoxFavorites.Any(f => f.VoiceId == v.VoiceId) })).ToArray();
        foreach (var choice in choices) panel.Children.Add(choice.Box);
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        var dialog = new ContentDialog { XamlRoot = Root.XamlRoot, Title = "VOICEVOXのお気に入り", PrimaryButtonText = "保存", CloseButtonText = "キャンセル", Content = new ScrollViewer { Content = panel, MaxHeight = 480 } };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            var previous = preferences.VoicevoxFavorites;
            try
            {
                preferences.VoicevoxFavorites = choices.Where(c => c.Box.IsChecked == true).Select(c => c.Voice).ToList();
                preferences.Save(AppPreferences.DefaultPath);
            }
            catch (Exception ex) { preferences.VoicevoxFavorites = previous; args.Cancel = true; error.Text = ex.Message; }
        };
        await dialog.ShowAsync();
    });
}
