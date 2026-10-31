using System.Text.Json;

namespace VideoEditor;

public static class McpDocumentation
{
    public static string Create(int port) => $$$$"""
        # Hikari Video Editor の外部操作

        Windows用の動画編集アプリです。画像・動画・音声をタイムラインに配置し、
        カット・複製・位置・大きさ・音量の調整、プレビュー再生、MP4出力ができます。

        ## 接続先
        ポート番号: {{{{port}}}}
        この説明: GET http://127.0.0.1:{{{{port}}}}/docs（本文なし）
        MCP操作: POST http://127.0.0.1:{{{{port}}}}/mcp
        このPC上からのみ接続できます。標準ポートは53100、使用中なら1ずつ増やします。

        ## リクエスト方法
        MCP Streamable HTTP / UTF-8 JSON-RPC 2.0を使用します。
        各メッセージを個別のHTTP POSTとして送信してください。
        Content-Type: application/json
        Accept: application/json, text/event-stream
        初期化後のヘッダー: MCP-Protocol-Version: 2025-11-25
        対応バージョン: 2025-11-25 / 2025-06-18 / 2025-03-26
        初期化応答のprotocolVersionを以後のヘッダーに指定してください。
        セッションID不要。応答はJSON、通知は本文なしのHTTP 202。
        GET /mcpのSSE配信は提供しません（405）。リクエスト本文上限は256 KiB。

        ## 接続と操作の例（各行を別のPOST本文として送信）
        1. 初期化:
        {"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-11-25","capabilities":{},"clientInfo":{"name":"editor-client","version":"1.0"}}}
        2. 初期化完了通知:
        {"jsonrpc":"2.0","method":"notifications/initialized"}
        3. ツール名・引数スキーマを取得:
        {"jsonrpc":"2.0","id":2,"method":"tools/list"}
        4. 現在のプロジェクトを取得:
        {"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"project_get","arguments":{}}}
        5. 素材を追加:
        {"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"clip_add","arguments":{"path":"C:\\Media\\movie.mp4"}}}
        6. 編集後のプレビューを更新:
        {"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"preview_refresh","arguments":{}}}

        ## 使用法
        最初にproject_getとplayback_getで状態を確認します。
        クリップ操作にはproject_getやclip_addの応答に含まれるIDを使います。
        pathはこのWindows PC上の絶対パス、時間は秒、レイヤーは1が最前面です。
        startはタイムライン上の開始時刻、trimは素材内の読み出し開始時刻、durationは使用する長さです。
        offsetX/offsetYはピクセル、scaleは倍率です。selection_moveのseconds/layersは相対移動量です。
        clip_splitのsecondsはプロジェクト上の絶対秒で、省略時は再生位置です。
        編集操作はUI操作と直列に実行し、タイムラインを更新します。プレビューはpreview_refreshで更新します。
        project_saveは既存プロジェクトを更新します。project_openは現在のプロジェクトを置き換えます。
        export_mp4は新しい出力先を指定し、完了まで応答を待ちます。既存ファイルの上書きは拒否します。
        ツール結果はresult.contentのtype=textのtext内にJSONとして格納します。
        ツール実行失敗はresult.isError=true、プロトコルエラーはerror.codeとerror.messageで返します。

        ## 音声・画像の作成
        speech_voicesでインストール済みのWindows音声（Id・Name・Language）を取得できます。
        speech_createは原稿textからWAVを作ります。voiceに音声IDまたは完全な名前を指定します。
        rateは-10〜10（既定0）、volumeは0〜100（既定100）です。原稿はプレーンテキストです。
        image_createはtitle（タイトル）・subtitle（副タイトル）・description（説明）・caption（字幕）と背景をPNGに描きます。4項目はすべて任意です。空欄は描画せず、全空欄なら背景のみ保存します。text/footerはdescription/captionの旧名として使えます。
        色background/textColor/accentは#RRGGBB、幅320〜3840、高さ240〜2160です。既定はプロジェクトの解像度です。
        templateはbasic（従来）、clean（すっきり解説）、science（サイエンス）、chalkboard（黒板スタイル）、gentle（やさしい雑学）です。図・キャラクターは描きません。
        fontFamilyはインストール済みフォント名（既定は設定のフォント）、fontSizeは12〜240px（既定64）で、収まるよう縮小します。
        作成機能はこのPC内で動作し、外部サービスやAPIキーは不要です。
        pathを省略するとワークスペースのGeneratedフォルダー（未設定ならドキュメント内）へ一意の名前で保存します。
        pathを指定する場合は存在するフォルダー内の新しい絶対パスを使ってください。既存ファイルは上書きしません。
        addToTimeline:trueで素材も追加します（既定false）。start省略時は再生位置です。
        画像のdurationは秒（既定5）、音声の長さは作成したWAVから取得します。応答はpathとclip（追加しない場合null）です。
        追加後はpreview_refreshでプレビューを更新してください。
        例: speech_create {"text":"こんにちは。","rate":0,"addToTimeline":true,"start":0}
        例: image_create {"title":"セキュリティの基本","subtitle":"今日からできること","description":"多要素認証を有効に","caption":"安全な設定を確認しましょう","duration":6,"addToTimeline":true,"start":0}

        ## ツール一覧と入力スキーマ
        {{{{JsonSerializer.Serialize(McpTools.All, new JsonSerializerOptions { WriteIndented = true })}}}}
        """;
}
