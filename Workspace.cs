using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.System;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private string? workspaceCurrentDirectory;
    private FileSystemWatcher? workspaceWatcher;
    private DispatcherTimer? workspaceRefreshTimer;
    private bool workspaceRefreshPending;

    private void WatchWorkspaceDirectory(string? directory)
    {
        if (string.Equals(workspaceWatcher?.Path, directory, StringComparison.OrdinalIgnoreCase)) return;
        workspaceWatcher?.Dispose();
        workspaceWatcher = null;
        workspaceRefreshTimer?.Stop();
        if (directory == null) return;
        workspaceRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        workspaceRefreshTimer.Tick -= WorkspaceRefreshTimer_Tick;
        workspaceRefreshTimer.Tick += WorkspaceRefreshTimer_Tick;
        var watcher = new FileSystemWatcher(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
        };
        watcher.Created += WorkspaceDirectory_Changed;
        watcher.Deleted += WorkspaceDirectory_Changed;
        watcher.Renamed += WorkspaceDirectory_Changed;
        watcher.Error += (_, _) => QueueWorkspaceRefresh(watcher);
        workspaceWatcher = watcher;
        watcher.EnableRaisingEvents = true;
    }

    private void WorkspaceDirectory_Changed(object sender, FileSystemEventArgs e) => QueueWorkspaceRefresh(sender);

    private void QueueWorkspaceRefresh(object watcher)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (closed || !ReferenceEquals(workspaceWatcher, watcher)) return;
            workspaceRefreshTimer!.Stop();
            workspaceRefreshTimer.Start();
        });
    }

    private async void WorkspaceRefreshTimer_Tick(object? sender, object e)
    {
        workspaceRefreshTimer!.Stop();
        if (closed) return;
        if (workspaceRefreshPending)
        {
            workspaceRefreshTimer.Start();
            return;
        }
        workspaceRefreshPending = true;
        try
        {
            await Run(async () =>
            {
                if (closed || workspaceCurrentDirectory == null) return;
                await RefreshWorkspaceNodes(WorkspaceFiles.RootNodes, workspaceCurrentDirectory);
            });
        }
        finally { workspaceRefreshPending = false; }
    }

    private async Task RefreshWorkspaceNodes(IList<TreeViewNode> nodes, string directory)
    {
        var entries = await Task.Run(() => WorkspaceEntry.ReadDirectory(directory));
        var existing = nodes.Where(n => n.Content is FrameworkElement { Tag: WorkspaceEntry })
            .ToDictionary(n => ((WorkspaceEntry)((FrameworkElement)n.Content).Tag).FullPath, StringComparer.OrdinalIgnoreCase);
        var updated = new List<TreeViewNode>();
        foreach (var entry in entries)
        {
            if (!existing.TryGetValue(entry.FullPath, out var node) ||
                ((WorkspaceEntry)((FrameworkElement)node.Content).Tag).IsDirectory != entry.IsDirectory)
                node = CreateWorkspaceNode(entry);
            if (entry.IsDirectory && node.IsExpanded)
            {
                try
                {
                    await RefreshWorkspaceNodes(node.Children, entry.FullPath);
                    node.HasUnrealizedChildren = false;
                }
                catch (DirectoryNotFoundException) { continue; }
            }
            else if (entry.IsDirectory)
            {
                node.Children.Clear();
                node.HasUnrealizedChildren = true;
            }
            updated.Add(node);
        }
        for (int index = nodes.Count - 1; index >= 0; index--)
            if (!updated.Contains(nodes[index])) nodes.RemoveAt(index);
        for (int index = 0; index < updated.Count; index++)
        {
            if (index < nodes.Count && ReferenceEquals(nodes[index], updated[index])) continue;
            nodes.Remove(updated[index]);
            nodes.Insert(index, updated[index]);
        }
    }
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
        UpdateGenerationAvailability();
        preferences.RememberWorkspace(directory);
        UpdateRecentWorkspacesMenu();
        UpdateStatusBar();
        preferences.Save(AppPreferences.DefaultPath);
    }

    private async Task LoadWorkspaceDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            WatchWorkspaceDirectory(null);
            WorkspaceFiles.RootNodes.Clear();
            WorkspacePath.Text = "";
            workspaceCurrentDirectory = null;
            return;
        }

        WatchWorkspaceDirectory(directory);
        var entries = await Task.Run(() => WorkspaceEntry.ReadDirectory(directory));
        workspaceCurrentDirectory = directory;
        WorkspacePath.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.IsNullOrEmpty(WorkspacePath.Text))
            WorkspacePath.Text = directory;
        ToolTipService.SetToolTip(WorkspacePath, directory);
        WorkspaceFiles.RootNodes.Clear();
        foreach (var entry in entries)
            WorkspaceFiles.RootNodes.Add(CreateWorkspaceNode(entry));
    }

    private TreeViewNode CreateWorkspaceNode(WorkspaceEntry entry)
    {
        var row = new Grid { ColumnSpacing = 8, Tag = entry, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = entry.Icon, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        var label = new TextBlock { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        ToolTipService.SetToolTip(row, entry.FullPath);
        if (!entry.IsDirectory)
        {
            var menu = new MenuFlyout();
            var add = new MenuFlyoutItem { Text = "タイムラインに追加" };
            add.Click += async (_, _) => await Run(async () =>
            {
                await Add(entry.FullPath, playhead);
                await Refresh();
            });
            var open = new MenuFlyoutItem { Text = MediaFiles.OpenLabel(MediaFiles.Kind(entry.FullPath)) };
            open.Click += async (_, _) => await Run(() => OpenMediaFile(entry.FullPath));
            menu.Items.Add(add);
            menu.Items.Add(open);
            row.ContextFlyout = menu;
        }
        return new TreeViewNode { Content = row, IsExpanded = false, HasUnrealizedChildren = entry.IsDirectory };
    }

    private async void WorkspaceFiles_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        var node = args.Node;
        if (!node.HasUnrealizedChildren || node.Content is not FrameworkElement { Tag: WorkspaceEntry entry }) return;
        await Run(async () =>
        {
            if (!node.HasUnrealizedChildren) return;
            try
            {
                var children = await Task.Run(() => WorkspaceEntry.ReadDirectory(entry.FullPath));
                node.Children.Clear();
                foreach (var child in children) node.Children.Add(CreateWorkspaceNode(child));
                node.HasUnrealizedChildren = false;
            }
            catch
            {
                node.IsExpanded = false;
                throw;
            }
        });
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
        if (WorkspaceFiles.SelectedNode?.Content is not FrameworkElement { Tag: WorkspaceEntry entry } || entry.IsDirectory)
            return;
        e.Handled = true;
        await Run(async () =>
        {
            await Add(entry.FullPath, playhead);
            await Refresh();
        });
    }

    private static async Task OpenMediaFile(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(path);
        if (!await Launcher.LaunchFileAsync(file))
            throw new InvalidOperationException("この素材を開く標準アプリが見つかりませんでした。");
    }
}


