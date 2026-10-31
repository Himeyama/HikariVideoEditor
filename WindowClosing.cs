using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private bool _closeApproved;
    private bool _closePending;

    private void Window_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeApproved)
            return;

        args.Cancel = true;
        if (_closePending)
            return;

        _closePending = true;
        Status.Text = "終了準備中: 実行中の処理の完了を待っています…";
        // Leave the native closing callback before displaying UI or closing again.
        if (!DispatcherQueue.TryEnqueue(async () => await ConfirmClose()))
            _closePending = false;
    }

    private async Task ConfirmClose()
    {
        using IDisposable operation = BeginStatusOperation();
        await _gate.WaitAsync();
        bool close = false;
        try
        {
            if (ProjectSnapshot() != _savedProjectSnapshot)
            {
                ContentDialog dialog = new()
                {
                    XamlRoot = Root.XamlRoot,
                    RequestedTheme = Root.ActualTheme,
                    Title = "変更を保存しますか？",
                    Content = "保存していない変更があります。",
                    PrimaryButtonText = "保存して閉じる",
                    SecondaryButtonText = "保存せずに閉じる",
                    CloseButtonText = "キャンセル",
                    DefaultButton = ContentDialogButton.Primary
                };
                ContentDialogResult result = await dialog.ShowAsync();
                if (result == ContentDialogResult.None)
                    return;
                if (result == ContentDialogResult.Primary)
                {
                    string? path = _savedProjectPath;
                    if (path == null)
                    {
                        using System.Windows.Forms.SaveFileDialog picker = new()
                        {
                            Title = "プロジェクトを保存",
                            FileName = "Project.hve",
                            Filter = "Hikari Project (*.hve)|*.hve",
                            DefaultExt = "hve",
                            AddExtension = true
                        };
                        System.Windows.Forms.NativeWindow owner = new();
                        owner.AssignHandle(WinRT.Interop.WindowNative.GetWindowHandle(this));
                        try
                        {
                            if (picker.ShowDialog(owner) != System.Windows.Forms.DialogResult.OK)
                                return;
                            path = picker.FileName;
                        }
                        finally
                        {
                            owner.ReleaseHandle();
                        }
                    }
                    Status.Text = "終了準備中: プロジェクトを保存しています…";
                    await Save(path);
                }
            }
            Status.Text = "終了処理中: 再生とアプリ連携を停止しています…";
            SavePanelLayout();
            _closed = true;
            _closeApproved = true;
            close = true;
        }
        catch (Exception exception)
        {
            Status.Text = $"終了できませんでした: {exception.Message}";
        }
        finally
        {
            _gate.Release();
            _closePending = false;
            if (!close && Status.Text.StartsWith("終了準備中:", StringComparison.Ordinal))
                Status.Text = "終了をキャンセルしました。";
        }
        if (close)
        {
            // Saving is complete. Release media resources after removing the window
            // so a slow native media shutdown does not leave an unresponsive window.
            AppWindow.Hide();
            Close();
        }
    }
}
