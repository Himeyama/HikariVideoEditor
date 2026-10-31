using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private CancellationTokenSource? _downloadCancellation;

    private async void DownloadVideo_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        using CancellationTokenSource cancellation = new();
        _downloadCancellation = cancellation;
        System.Net.CookieContainer cookies = new();
        // Redirects are deliberately disabled: only validated media hosts may receive requests.
        using HttpClient http = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            CookieContainer = cookies,
            AutomaticDecompression = System.Net.DecompressionMethods.All
        }) { Timeout = TimeSpan.FromMinutes(30) };
        try
        {
            await ShowVideoDownloadAsync(http, cookies, cancellation);
        }
        catch (OperationCanceledException)
        {
            if (!_closed) Status.Text = "動画のダウンロードをキャンセルしました。";
        }
        catch (Exception exception)
        {
            if (!_closed)
            {
                Status.Text = "動画のダウンロードに失敗しました。";
                ContentDialog error = new()
                {
                    XamlRoot = Root.XamlRoot, Title = "動画をダウンロードできませんでした",
                    Content = DownloadError(exception), CloseButtonText = "閉じる"
                };
                await error.ShowAsync();
            }
        }
        finally
        {
            _downloadCancellation = null;
        }
    });

    private async Task ShowVideoDownloadAsync(HttpClient http, System.Net.CookieContainer cookies, CancellationTokenSource cancellation)
    {
        CancellationToken token = cancellation.Token;
        TextBox input = new() { Header = "動画の URL", PlaceholderText = "https://…", HorizontalAlignment = HorizontalAlignment.Stretch };
        Button fetch = new() { Content = "動画情報を取得" };
        TextBlock details = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        ComboBox quality = new() { Header = "画質", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        CheckBox add = new() { Content = "完了後にタイムラインへ追加", IsChecked = false };
        TextBlock error = new() { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        string? workspaceDirectory = _workspaceCurrentDirectory ?? _project.WorkspaceDirectory;
        bool hasWorkspace = !string.IsNullOrWhiteSpace(workspaceDirectory) && Directory.Exists(workspaceDirectory);
        TextBlock workspaceHint = new()
        {
            Text = hasWorkspace ? $"ワークスペースの保存先: {workspaceDirectory}" : "ワークスペースを選択すると、直接ダウンロードできます。",
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
        };
        StackPanel panel = new() { Spacing = 16, MinWidth = 360, MaxWidth = 520 };
        panel.Children.Add(input);
        panel.Children.Add(fetch);
        panel.Children.Add(details);
        panel.Children.Add(quality);
        panel.Children.Add(add);
        panel.Children.Add(workspaceHint);
        panel.Children.Add(error);
        ContentDialog dialog = new()
        {
            XamlRoot = Root.XamlRoot, Title = "動画をダウンロード",
            Content = new ScrollViewer { Content = panel, MaxHeight = Math.Max(200, AppHost.ActualHeight - 240) },
            PrimaryButtonText = "保存先を選択", IsPrimaryButtonEnabled = false,
            SecondaryButtonText = "ワークスペースにダウンロード", IsSecondaryButtonEnabled = false,
            CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.None
        };
        DownloadVideo? video = null;
        bool resolving = false;
        bool closing = false;
        input.TextChanged += (_, _) =>
        {
            video = null;
            quality.Items.Clear();
            quality.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;
            details.Text = "";
            error.Text = "";
        };
        dialog.Closing += (_, args) =>
        {
            if (resolving)
            {
                args.Cancel = true;
                closing = true;
                cancellation.Cancel();
            }
        };
        fetch.Click += async (_, _) =>
        {
            resolving = true;
            input.IsEnabled = fetch.IsEnabled = quality.IsEnabled = false;
            dialog.IsPrimaryButtonEnabled = false;
            dialog.IsSecondaryButtonEnabled = false;
            video = null;
            quality.Items.Clear();
            error.Text = "";
            details.Text = "動画情報を取得中…";
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                Progress<DownloadProgress> progress = new(value =>
                {
                    if (resolving && !_closed && !token.IsCancellationRequested) details.Text = value.Stage;
                });
                // Validate before starting either network or browser work.
                _ = VideoDownloadSource.ParseId(input.Text);
                try
                {
                    using CancellationTokenSource directTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    directTimeout.CancelAfter(TimeSpan.FromSeconds(40));
                    video = await new VideoDownloadSource(http).ResolveAsync(input.Text, directTimeout.Token, progress);
                }
                catch (Exception exception) when (!timeout.IsCancellationRequested &&
                    exception is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
                {
                    video = await ResolveInAnonymousBrowserAsync(input.Text, http, cookies, timeout.Token, progress);
                }
                details.Text = $"{video.Title}\n長さ: {video.Duration:hh\\:mm\\:ss}";
                foreach (DownloadStream format in video.Formats) quality.Items.Add(format);
                quality.SelectedIndex = Math.Max(0, video.Formats.ToList().FindIndex(f => f.Height <= 1080));
                quality.IsEnabled = true;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.IsSecondaryButtonEnabled = hasWorkspace;
            }
            catch (OperationCanceledException)
            {
                details.Text = "";
                if (!token.IsCancellationRequested) error.Text = "動画情報の取得がタイムアウトしました。再試行してください。";
            }
            catch (Exception exception)
            {
                details.Text = "";
                error.Text = DownloadError(exception);
            }
            finally
            {
                resolving = false;
                input.IsEnabled = fetch.IsEnabled = true;
                if (closing || _closed) dialog.Hide();
            }
        };
        ContentDialogResult choice = await dialog.ShowAsync();
        if (choice is not (ContentDialogResult.Primary or ContentDialogResult.Secondary) || video == null || quality.SelectedItem is not DownloadStream formatChoice)
            return;
        token.ThrowIfCancellationRequested();
        bool saveToWorkspace = choice == ContentDialogResult.Secondary;
        string destinationPath;
        if (saveToWorkspace)
        {
            if (!hasWorkspace) throw new ArgumentException("ワークスペースを選択してください。");
            destinationPath = VideoDownloadDestination.InWorkspace(workspaceDirectory!, video.Title);
        }
        else
        {
            FileSavePicker picker = new() { SuggestedFileName = VideoDownloadDestination.FileName(video.Title), DefaultFileExtension = ".mp4" };
            picker.FileTypeChoices.Add("MP4 動画", new[] { ".mp4" });
            InitPicker(picker);
            Windows.Storage.StorageFile destination = await picker.PickSaveFileAsync();
            if (destination == null) return;
            destinationPath = destination.Path;
        }
        if (_project.Clips.Any(c => c.Kind != "subtitle" && string.Equals(Path.GetFullPath(c.Path), destinationPath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("使用中の素材には上書きできません。別の保存先を選んでください。");
        TextBlock label = new() { Text = "ダウンロードを準備中…", TextWrapping = TextWrapping.Wrap };
        ProgressBar bar = new() { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
        StackPanel content = new() { Spacing = 16, MinWidth = 360 };
        content.Children.Add(label);
        content.Children.Add(bar);
        ContentDialog progressDialog = new()
        {
            XamlRoot = Root.XamlRoot, Title = "動画をダウンロード中", Content = content, CloseButtonText = "キャンセル"
        };
        bool finished = false;
        progressDialog.Closing += (_, args) =>
        {
            if (finished) return;
            args.Cancel = true;
            cancellation.Cancel();
            label.Text = "キャンセル中…";
            progressDialog.IsEnabled = false;
        };
        Windows.Foundation.IAsyncOperation<ContentDialogResult> showing = progressDialog.ShowAsync();
        Progress<DownloadProgress> progress = new(value =>
        {
            if (finished || _closed || token.IsCancellationRequested) return;
            bar.IsIndeterminate = !value.Percent.HasValue;
            bar.Value = value.Percent ?? 0;
            label.Text = value.Percent.HasValue ? $"{value.Stage}: {value.Percent:F0}%" : value.Stage;
        });
        try
        {
            await VideoDownloadWriter.WriteAsync(new VideoDownloadTransfer(http), video, formatChoice, destinationPath, progress, token, overwrite: !saveToWorkspace);
        }
        finally
        {
            finished = true;
            progressDialog.Hide();
            await showing;
        }
        if (_closed) return;
        // Saving is already complete. An import failure must not be reported as a transfer failure.
        try
        {
            if (_workspaceCurrentDirectory != null) await LoadWorkspaceDirectory(_workspaceCurrentDirectory);
            if (add.IsChecked == true)
            {
                await Add(destinationPath, _playhead);
                await Refresh();
            }
            Status.Text = $"ダウンロード完了: {destinationPath}";
        }
        catch
        {
            Status.Text = $"保存は完了しました。素材を手動で追加してください: {destinationPath}";
        }
    }

    private static string DownloadError(Exception exception) => exception switch
    {
        ArgumentException or InvalidOperationException => exception.Message,
        HttpRequestException => "配信元と通信できませんでした。接続を確認して再試行してください。",
        IOException or UnauthorizedAccessException => "ファイルを保存できませんでした。空き容量と保存先のアクセス権を確認してください。",
        _ => "動画を処理できませんでした。別の動画または画質で再試行してください。"
    };
}
