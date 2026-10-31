# Windows インストーラーとリリース

NSIS 3 で Windows x64 向けのインストーラーを作成します。Windows 10 バージョン 2004 以降が対象です。.NET と Windows App SDK は同梱されます。管理者権限は不要で、ユーザーごとの `%LOCALAPPDATA%\Programs\VideoEditor` にインストールし、スタートメニューと「インストールされているアプリ」に登録します。

## GitHub で配布

変更を GitHub のリポジトリへ push した後、次のようにタグを作成して push します。

```powershell
git tag v0.1.0
git push origin v0.1.0
```

`.github/workflows/release.yml` が起動し、Release ビルド、NSIS インストーラー作成、SHA-256 チェックサム作成、GitHub Release 公開を実行します。Release に次のファイルが添付されます。

- `VideoEditor-0.1.0-win-x64-Setup.exe`
- `VideoEditor-0.1.0-win-x64-Setup.exe.sha256`

バージョンはタグから設定します。正式版の `v数字.数字.数字` 形式に対応し、各数値は 65535 以下です。リリース済みタグは再利用せず、新しいバージョンタグを作成してください。Actions がリポジトリで有効である必要があります。標準の `GITHUB_TOKEN` を使用するため追加のシークレットは不要です。

Actions の **Build Windows installer → Run workflow** でバージョン番号を入力すると、Release を公開せずにインストーラーを Artifacts に保存できます。

## ローカルで作成

Visual Studio 2022 のビルド環境、.NET 8 SDK、NSIS 3 が必要です。

```powershell
choco install nsis -y
./installer/build-installer.ps1 -Version 0.1.0
```

出力先は `artifacts/installer` です。`-OutputDirectory` と `-MakeNsis` で出力先や NSIS のパスを指定できます。既存の `build.ps1` から Visual Studio の MSBuild を呼び出して発行します。インストーラー作成中はアプリを起動しません。

発行時には、WinUI の `VideoEditor.pri` と各画面の `.xbf` を明示的に公開先へ含めます。インストーラーの作成前にこれらが存在し、空でないことを検査します。通常のビルド成功だけでは配布版の起動確認にはならないため、配布前にはインストーラーから展開したアプリでもウィンドウが開くことを確認してください。

インストール先の変更・更新・アンインストールの前にアプリを閉じてください。プロジェクトや元動画をインストール先の `app` フォルダーには保存しないでください。このフォルダーはアンインストールで削除されます。ユーザー設定や外部に保存したプロジェクトは削除しません。

この構成ではコード署名を行いません。署名証明書による署名を追加するまでは、ダウンロードした実行ファイルに Windows の警告が出る場合があります。

実装の参照: [GitHub のタグ push イベント](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows)、[Windows App SDK の自己完結型配置](https://learn.microsoft.com/en-us/windows/apps/package-and-deploy/self-contained-deploy/deploy-self-contained-apps)、[NSIS マニュアル](https://nsis.sourceforge.io/Docs/Chapter4.html)。
