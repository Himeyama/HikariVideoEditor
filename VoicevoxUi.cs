using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer _voicevoxTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _checkingVoicevox;
    private string? _voicevoxExecutable;
    private bool _voicevoxRunning;
    private DateTime _voicevoxStartingUntil;

    private void InitializeVoicevox()
    {
        _voicevoxTimer.Tick += async (_, _) => await UpdateVoicevoxStatus();
        Closed += (_, _) => _voicevoxTimer.Stop();
        _voicevoxTimer.Start();
        _ = UpdateVoicevoxStatus();
    }

    private async Task UpdateVoicevoxStatus()
    {
        if (_checkingVoicevox || _closed) return;
        _checkingVoicevox = true;
        try
        {
            _voicevoxExecutable = await Task.Run(Voicevox.FindExecutable);
            _voicevoxRunning = await Voicevox.IsRunning();
            if (_closed) return;
            VoicevoxStatusButton.Visibility = _voicevoxExecutable != null || _voicevoxRunning ? Visibility.Visible : Visibility.Collapsed;
            VoicevoxStatus.Text = _voicevoxRunning ? "VOICEVOX" : DateTime.UtcNow < _voicevoxStartingUntil ? "VOICEVOX: 起動中…" : "VOICEVOX: 未起動";
            ToolTipService.SetToolTip(VoicevoxStatusButton, _voicevoxRunning ? "キャラクターと声のお気に入りを設定" : "クリックしてVOICEVOXを起動");
        }
        catch (Exception ex) { if (!_closed) Status.Text = $"VOICEVOXの確認に失敗しました: {ex.Message}"; }
        finally { _checkingVoicevox = false; }
    }

    private async void VoicevoxStatus_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        await UpdateVoicevoxStatus();
        if (!_voicevoxRunning)
        {
            if (DateTime.UtcNow < _voicevoxStartingUntil) return;
            if (_voicevoxExecutable == null) throw new InvalidOperationException("VOICEVOXが見つかりません。");
            using System.Diagnostics.Process? process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_voicevoxExecutable) { UseShellExecute = true });
            _voicevoxStartingUntil = DateTime.UtcNow.AddSeconds(60);
            VoicevoxStatus.Text = "VOICEVOX: 起動中…";
            Status.Text = "VOICEVOXを起動しています。準備ができると表示が切り替わります。";
            return;
        }
        VoicevoxFavorite[] voices = await Voicevox.Voices();
        StackPanel panel = new() { Spacing = 12, Width = 440 };
        panel.Children.Add(new TextBlock { Text = "「音声を作成」で使うキャラクターと声にチェックを入れてください。", TextWrapping = TextWrapping.Wrap });
        (VoicevoxFavorite Voice, CheckBox Box)[] choices = voices.Select(v => (Voice: v, Box: new CheckBox { Content = $"{v.Character}（{v.Style}）", IsChecked = _preferences.VoicevoxFavorites.Any(f => f.VoiceId == v.VoiceId) })).ToArray();
        foreach ((VoicevoxFavorite Voice, CheckBox Box) choice in choices) panel.Children.Add(choice.Box);
        TextBlock error = new() { TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(error);
        ContentDialog dialog = new() { XamlRoot = Root.XamlRoot, Title = "VOICEVOXのお気に入り", PrimaryButtonText = "保存", CloseButtonText = "キャンセル", Content = new ScrollViewer { Content = panel, MaxHeight = 480 } };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            List<VoicevoxFavorite> previous = _preferences.VoicevoxFavorites;
            try
            {
                _preferences.VoicevoxFavorites = choices.Where(c => c.Box.IsChecked == true).Select(c => c.Voice).ToList();
                _preferences.Save(AppPreferences.DefaultPath);
            }
            catch (Exception ex) { _preferences.VoicevoxFavorites = previous; args.Cancel = true; error.Text = ex.Message; }
        };
        await dialog.ShowAsync();
    });
}
