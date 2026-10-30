using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.System;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private string? workspaceCurrentDirectory;
    private async void ChooseWorkspace_Click(object sender, RoutedEventArgs e)
    {
        var browser = new FolderBrowser();
        string initial = project.WorkspaceDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        await browser.Navigate(initial);
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Title = "ワークスペースのフォルダーを選択",
            Content = browser,
            PrimaryButtonText = "このフォルダーを選択",
            CloseButtonText = "キャンセル",
            DefaultButton = ContentDialogButton.Primary
        };
        dialog.PrimaryButtonClick += async (_, args) =>
        {
            var deferral = args.GetDeferral();
            try
            {
                args.Cancel = !await browser.ConfirmSelection();
            }
            finally
            {
                deferral.Complete();
            }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || browser.CurrentDirectory == null)
            return;
        await Run(async () =>
        {
            await SelectWorkspace(browser.CurrentDirectory);
        });
    }

    private void UpdateRecentWorkspacesMenu()
    {
        RecentWorkspacesMenu.Items.Clear();
        foreach (var directory in (preferences.RecentWorkspaces ?? new())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
        {
            var item = new MenuFlyoutItem { Text = directory };
            ToolTipService.SetToolTip(item, directory);
            item.Click += async (_, _) => await Run(() => SelectWorkspace(directory));
            RecentWorkspacesMenu.Items.Add(item);
        }
        if (RecentWorkspacesMenu.Items.Count == 0)
            RecentWorkspacesMenu.Items.Add(new MenuFlyoutItem { Text = "履歴はありません", IsEnabled = false });
    }

    private async Task SelectWorkspace(string directory)
    {
        directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        await LoadWorkspaceDirectory(directory);
        Remember();
        project.WorkspaceDirectory = directory;
        preferences.RememberWorkspace(directory);
        UpdateRecentWorkspacesMenu();
        UpdateStatusBar();
        preferences.Save(AppPreferences.DefaultPath);
    }

    private async Task LoadWorkspaceDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            WorkspaceFiles.Items.Clear();
            WorkspacePath.Text = "";
            workspaceCurrentDirectory = null;
            return;
        }

        var entries = await Task.Run(() => WorkspaceEntry.ReadDirectory(directory));
        workspaceCurrentDirectory = directory;
        WorkspacePath.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.IsNullOrEmpty(WorkspacePath.Text))
            WorkspacePath.Text = directory;
        ToolTipService.SetToolTip(WorkspacePath, directory);
        // Each row owns its menu so the menu always refers to the clicked file.
        WorkspaceFiles.Items.Clear();
        foreach (var entry in entries)
        {
            var row = new ListViewItem
            {
                Content = entry,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Tag = entry,
                ContentTemplate = WorkspaceFiles.ItemTemplate
            };
            ToolTipService.SetToolTip(row, entry.Name);
            if (!entry.IsDirectory)
            {
                var menu = new MenuFlyout();
                var add = new MenuFlyoutItem
                {
                    Text = "タイムラインに追加"
                };
                add.Click += async (_, _) => await Run(async () =>
                {
                    await Add(entry.FullPath, playhead);
                    await Refresh();
                });
                var open = new MenuFlyoutItem
                {
                    Text = MediaFiles.OpenLabel(MediaFiles.Kind(entry.FullPath))
                };
                open.Click += async (_, _) => await Run(() => OpenMediaFile(entry.FullPath));
                menu.Items.Add(add);
                menu.Items.Add(open);
                row.ContextFlyout = menu;
            }

            WorkspaceFiles.Items.Add(row);
        }
    }

    private async void WorkspaceRefresh_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (workspaceCurrentDirectory != null)
            await LoadWorkspaceDirectory(workspaceCurrentDirectory);
    });

    private async void WorkspaceParent_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (workspaceCurrentDirectory != null && Directory.GetParent(workspaceCurrentDirectory)is { } parent)
            await LoadWorkspaceDirectory(parent.FullName);
    });
    private async void WorkspaceFiles_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (WorkspaceFiles.SelectedItem is not ListViewItem { Tag: WorkspaceEntry entry })
            return;
        await Run(async () =>
        {
            if (entry.IsDirectory)
                await LoadWorkspaceDirectory(entry.FullPath);
            else
            {
                await Add(entry.FullPath, playhead);
                await Refresh();
            }
        });
    }

    private static async Task OpenMediaFile(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (!await Launcher.LaunchFileAsync(file))
            throw new InvalidOperationException("この素材を開く標準アプリが見つかりませんでした。");
    }
}


