using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _exportCancellation;

    private async Task Export(string path)
    {
        await VideoExport.Write(_project, path, progress: new Progress<double>(value =>
        {
            if (!_closed)
                Status.Text = $"動画を書き出し中: {value:F0}%";
        }));
        Status.Text = $"書き出し完了: {Path.GetFullPath(path)}";
    }

    private async void Export_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_project.Clips.Count == 0)
            throw new ArgumentException("素材を追加してください。");
        EditProject snapshot = _project.Copy();
        snapshot.Validate();
        ComboBox quality = new() { Header = "画質", SelectedIndex = 1, HorizontalAlignment = HorizontalAlignment.Stretch };
        quality.Items.Add("標準（5 Mbps）");
        quality.Items.Add("高画質（10 Mbps）");
        quality.Items.Add("最高画質（20 Mbps）");
        StackPanel content = new() { Spacing = 16, MinWidth = 320 };
        content.Children.Add(new TextBlock
        {
            Text = $"MP4（H.264 / AAC）\n{snapshot.Video.Width} × {snapshot.Video.Height} / {snapshot.Video.FrameRate} fps\n音声: {snapshot.Video.AudioSampleRate / 1000.0:0.##} kHz\nタイムライン全体を書き出します。",
            TextWrapping = TextWrapping.Wrap
        });
        content.Children.Add(quality);
        ContentDialog settings = new()
        {
            XamlRoot = Root.XamlRoot,
            Title = "動画を書き出す",
            Content = content,
            PrimaryButtonText = "保存先を選択",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        if (await settings.ShowAsync() != ContentDialogResult.Primary)
            return;
        FileSavePicker picker = new() { SuggestedFileName = "Movie", DefaultFileExtension = ".mp4" };
        InitPicker(picker);
        picker.FileTypeChoices.Add("MP4 動画", new[] { ".mp4" });
        Windows.Storage.StorageFile file = await picker.PickSaveFileAsync();
        if (file == null)
            return;

        using CancellationTokenSource cancellation = new();
        _exportCancellation = cancellation;
        TextBlock label = new() { Text = "動画を準備中…", TextWrapping = TextWrapping.Wrap };
        ProgressBar bar = new() { Minimum = 0, Maximum = 100, IsIndeterminate = true };
        StackPanel progressContent = new() { Spacing = 16, MinWidth = 320 };
        progressContent.Children.Add(label);
        progressContent.Children.Add(bar);
        ContentDialog dialog = new()
        {
            XamlRoot = Root.XamlRoot,
            Title = "動画を書き出し中",
            Content = progressContent,
            CloseButtonText = "キャンセル"
        };
        bool finished = false;
        dialog.Closing += (_, args) => args.Cancel = !finished;
        bool acceptingProgress = true;
        dialog.CloseButtonClick += (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
            label.Text = "キャンセル中…";
            dialog.IsEnabled = false;
        };
        Windows.Foundation.IAsyncOperation<ContentDialogResult> showing = dialog.ShowAsync();
        Progress<double> progress = new(value =>
        {
            if (!acceptingProgress || cancellation.IsCancellationRequested || _closed)
                return;
            bar.IsIndeterminate = false;
            bar.Value = Math.Clamp(value, 0, 100);
            label.Text = $"書き出し中: {bar.Value:F0}%";
        });
        try
        {
            uint bitrate = new uint[] { 5_000_000, 10_000_000, 20_000_000 }[quality.SelectedIndex];
            await VideoExport.Write(snapshot, file.Path, overwrite: true, videoBitrate: bitrate,
                progress: progress, cancellationToken: cancellation.Token);
            Status.Text = $"書き出し完了: {file.Path}";
        }
        catch (OperationCanceledException)
        {
            Status.Text = "動画の書き出しをキャンセルしました。";
        }
        finally
        {
            acceptingProgress = false;
            _exportCancellation = null;
            finished = true;
            dialog.Hide();
            await showing;
        }
    });
}

