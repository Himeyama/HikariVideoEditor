using System.Net.Http;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private CancellationTokenSource? downloadCancellation;

    private async void DownloadVideo_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        using var cancellation = new CancellationTokenSource();
        downloadCancellation = cancellation;
        var cookies = new System.Net.CookieContainer();
        // Redirects are deliberately disabled: only validated media hosts may receive requests.
        using var http = new HttpClient(new HttpClientHandler
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
            if (!closed) Status.Text = "動画のダウンロードをキャンセルしました。";
        }
        catch (Exception exception)
        {
            if (!closed)
            {
                Status.Text = "動画のダウンロードに失敗しました。";
                var error = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot, Title = "動画をダウンロードできませんでした",
                    Content = DownloadError(exception), CloseButtonText = "閉じる"
                };
                await error.ShowAsync();
            }
        }
        finally
        {
            downloadCancellation = null;
        }
    });

    private async Task ShowVideoDownloadAsync(HttpClient http, System.Net.CookieContainer cookies, CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var input = new TextBox { Header = "動画の URL", PlaceholderText = "https://…", HorizontalAlignment = HorizontalAlignment.Stretch };
        var fetch = new Button { Content = "動画情報を取得" };
        var details = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var quality = new ComboBox { Header = "画質", HorizontalAlignment = HorizontalAlignment.Stretch, IsEnabled = false };
        var add = new CheckBox { Content = "完了後にタイムラインへ追加", IsChecked = false };
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        var workspaceDirectory = workspaceCurrentDirectory ?? project.WorkspaceDirectory;
        bool hasWorkspace = !string.IsNullOrWhiteSpace(workspaceDirectory) && Directory.Exists(workspaceDirectory);
        var workspaceHint = new TextBlock
        {
            Text = hasWorkspace ? $"ワークスペースの保存先: {workspaceDirectory}" : "ワークスペースを選択すると、直接ダウンロードできます。",
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
        };
        var panel = new StackPanel { Spacing = 16, MinWidth = 360, MaxWidth = 520 };
        panel.Children.Add(input);
        panel.Children.Add(fetch);
        panel.Children.Add(details);
        panel.Children.Add(quality);
        panel.Children.Add(add);
        panel.Children.Add(workspaceHint);
        panel.Children.Add(error);
        var dialog = new ContentDialog
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
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(120));
                var progress = new Progress<DownloadProgress>(value =>
                {
                    if (resolving && !closed && !token.IsCancellationRequested) details.Text = value.Stage;
                });
                // Validate before starting either network or browser work.
                _ = VideoDownloadSource.ParseId(input.Text);
                try
                {
                    using var directTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    directTimeout.CancelAfter(TimeSpan.FromSeconds(40));
                    video = await new VideoDownloadSource(http).ResolveAsync(input.Text, directTimeout.Token, progress);
                }
                catch (Exception exception) when (!timeout.IsCancellationRequested &&
                    exception is InvalidOperationException or System.Net.Http.HttpRequestException or System.Text.Json.JsonException or OperationCanceledException)
                {
                    video = await ResolveInAnonymousBrowserAsync(input.Text, http, cookies, timeout.Token, progress);
                }
                details.Text = $"{video.Title}\n長さ: {video.Duration:hh\\:mm\\:ss}";
                foreach (var format in video.Formats) quality.Items.Add(format);
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
                if (closing || closed) dialog.Hide();
            }
        };
        var choice = await dialog.ShowAsync();
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
            var picker = new FileSavePicker { SuggestedFileName = VideoDownloadDestination.FileName(video.Title), DefaultFileExtension = ".mp4" };
            picker.FileTypeChoices.Add("MP4 動画", new[] { ".mp4" });
            InitPicker(picker);
            var destination = await picker.PickSaveFileAsync();
            if (destination == null) return;
            destinationPath = destination.Path;
        }
        if (project.Clips.Any(c => c.Kind != "subtitle" && string.Equals(Path.GetFullPath(c.Path), destinationPath, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("使用中の素材には上書きできません。別の保存先を選んでください。");
        var label = new TextBlock { Text = "ダウンロードを準備中…", TextWrapping = TextWrapping.Wrap };
        var bar = new ProgressBar { IsIndeterminate = true, Minimum = 0, Maximum = 100 };
        var content = new StackPanel { Spacing = 16, MinWidth = 360 };
        content.Children.Add(label);
        content.Children.Add(bar);
        var progressDialog = new ContentDialog
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
        var showing = progressDialog.ShowAsync();
        var progress = new Progress<DownloadProgress>(value =>
        {
            if (finished || closed || token.IsCancellationRequested) return;
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
        if (closed) return;
        // Saving is already complete. An import failure must not be reported as a transfer failure.
        try
        {
            if (workspaceCurrentDirectory != null) await LoadWorkspaceDirectory(workspaceCurrentDirectory);
            if (add.IsChecked == true)
            {
                await Add(destinationPath, playhead);
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
