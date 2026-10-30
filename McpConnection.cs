using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private async void McpConnection_Click(object sender, RoutedEventArgs e)
    {
        var prompt = $"""
            Hikari Video Editor は、画像・動画・音声をタイムライン上に配置し、カット、複製、位置・大きさ・音量の調整、プレビュー再生、MP4出力ができるWindows用の動画編集アプリです。このPCで起動中のアプリを操作してください。

            ポート番号: {bridge.Port}
            仕様・リクエスト方法・使用法の取得先: http://127.0.0.1:{bridge.Port}/docs
            HTTPメソッド: GET（本文なし）。最初にこのURLを取得し、接続手順とツールの仕様を確認してください。

            操作リクエストの送信先: {bridge.Endpoint}
            HTTPメソッド: POST
            通信方式: MCP Streamable HTTP（JSON-RPC 2.0）。MCP対応クライアントにはこの送信先URLを登録してください。
            Content-Type: application/json
            Accept: application/json, text/event-stream
            initialize → notifications/initialized → tools/list → tools/call の順に利用してください。
            最初に project_get と playback_get で現在の状態を確認し、編集後は preview_refresh を呼び出してください。
            接続先はこのPC内からのみ利用できます。このPCのローカルHTTPに接続できる環境で実行してください。

            編集の依頼:
            （ここに依頼内容を追記してください）
            """;
        var text = new TextBlock
        {
            Text = prompt, IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap
        };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot, Title = "生成AIへの接続",
            Content = new ScrollViewer
            {
                Content = text, MinWidth = 400, MaxHeight = 400,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            },
            PrimaryButtonText = "プロンプトをコピー", CloseButtonText = "閉じる"
        };
        dialog.PrimaryButtonClick += (_, args) =>
        {
            args.Cancel = true;
            var data = new DataPackage();
            data.SetText(prompt);
            Clipboard.SetContent(data);
            dialog.PrimaryButtonText = "コピーしました";
        };
        await dialog.ShowAsync();
    }
}
