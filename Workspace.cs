using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.System;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private string? _workspaceCurrentDirectory;
    private FileSystemWatcher? _workspaceWatcher;
    private DispatcherTimer? _workspaceRefreshTimer;
    private bool _workspaceRefreshPending;

    private void WatchWorkspaceDirectory(string? directory)
    {
        if (string.Equals(_workspaceWatcher?.Path, directory, StringComparison.OrdinalIgnoreCase)) return;
        _workspaceWatcher?.Dispose();
        _workspaceWatcher = null;
        _workspaceRefreshTimer?.Stop();
        if (directory == null) return;
        _workspaceRefreshTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _workspaceRefreshTimer.Tick -= WorkspaceRefreshTimer_Tick;
        _workspaceRefreshTimer.Tick += WorkspaceRefreshTimer_Tick;
        FileSystemWatcher watcher = new(directory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
        };
        watcher.Created += WorkspaceDirectory_Changed;
        watcher.Deleted += WorkspaceDirectory_Changed;
        watcher.Renamed += WorkspaceDirectory_Changed;
        watcher.Error += (_, _) => QueueWorkspaceRefresh(watcher);
        _workspaceWatcher = watcher;
        watcher.EnableRaisingEvents = true;
    }

    private void WorkspaceDirectory_Changed(object sender, FileSystemEventArgs e) => QueueWorkspaceRefresh(sender);

    private void QueueWorkspaceRefresh(object watcher)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (_closed || !ReferenceEquals(_workspaceWatcher, watcher)) return;
            _workspaceRefreshTimer!.Stop();
            _workspaceRefreshTimer.Start();
        });
    }

    private async void WorkspaceRefreshTimer_Tick(object? sender, object e)
    {
        _workspaceRefreshTimer!.Stop();
        if (_closed) return;
        if (_workspaceRefreshPending)
        {
            _workspaceRefreshTimer.Start();
            return;
        }
        _workspaceRefreshPending = true;
        try
        {
            await Run(async () =>
            {
                if (_closed || _workspaceCurrentDirectory == null) return;
                await RefreshWorkspaceNodes(WorkspaceFiles.RootNodes, _workspaceCurrentDirectory);
            });
        }
        finally { _workspaceRefreshPending = false; }
    }

    private async Task RefreshWorkspaceNodes(IList<TreeViewNode> nodes, string directory)
    {
        List<WorkspaceEntry> entries = await Task.Run(() => WorkspaceEntry.ReadDirectory(directory));
        Dictionary<string, TreeViewNode> existing = nodes.Where(n => n.Content is FrameworkElement { Tag: WorkspaceEntry })
            .ToDictionary(n => ((WorkspaceEntry)((FrameworkElement)n.Content).Tag).FullPath, StringComparer.OrdinalIgnoreCase);
        List<TreeViewNode> updated = new();
        foreach (WorkspaceEntry entry in entries)
        {
            if (!existing.TryGetValue(entry.FullPath, out TreeViewNode? node) ||
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
        FolderBrowser browser = new();
        string initial = _project.WorkspaceDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        await browser.Navigate(initial);
        ContentDialog dialog = new()
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
            ContentDialogButtonClickDeferral deferral = args.GetDeferral();
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
        foreach (string directory in (_preferences.RecentWorkspaces ?? new())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(20))
        {
            MenuFlyoutItem item = new() { Text = directory };
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
        _project.WorkspaceDirectory = directory;
        UpdateGenerationAvailability();
        _preferences.RememberWorkspace(directory);
        UpdateRecentWorkspacesMenu();
        UpdateStatusBar();
        _preferences.Save(AppPreferences.DefaultPath);
    }

    private async Task LoadWorkspaceDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            WatchWorkspaceDirectory(null);
            WorkspaceFiles.RootNodes.Clear();
            WorkspacePath.Text = "";
            _workspaceCurrentDirectory = null;
            return;
        }

        WatchWorkspaceDirectory(directory);
        List<WorkspaceEntry> entries = await Task.Run(() => WorkspaceEntry.ReadDirectory(directory));
        _workspaceCurrentDirectory = directory;
        WorkspacePath.Text = Path.GetFileName(Path.TrimEndingDirectorySeparator(directory));
        if (string.IsNullOrEmpty(WorkspacePath.Text))
            WorkspacePath.Text = directory;
        ToolTipService.SetToolTip(WorkspacePath, directory);
        WorkspaceFiles.RootNodes.Clear();
        foreach (WorkspaceEntry entry in entries)
            WorkspaceFiles.RootNodes.Add(CreateWorkspaceNode(entry));
    }

    private TreeViewNode CreateWorkspaceNode(WorkspaceEntry entry)
    {
        Grid row = new() { ColumnSpacing = 8, Tag = entry, HorizontalAlignment = HorizontalAlignment.Stretch };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.Children.Add(new FontIcon { Glyph = entry.Icon, FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
        TextBlock label = new() { Text = entry.Name, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(label, 1);
        row.Children.Add(label);
        ToolTipService.SetToolTip(row, entry.FullPath);
        MenuFlyout menu = new();
        if (!entry.IsDirectory && MediaFiles.IsSupported(entry.FullPath))
        {
            MenuFlyoutItem add = new() { Text = "タイムラインに追加" };
            add.Click += async (_, _) => await Run(async () =>
            {
                await Add(entry.FullPath, _playhead);
                await Refresh();
            });
            MenuFlyoutItem open = new() { Text = MediaFiles.OpenLabel(MediaFiles.Kind(entry.FullPath)) };
            open.Click += async (_, _) => await Run(() => OpenMediaFile(entry.FullPath));
            menu.Items.Add(add);
            menu.Items.Add(open);
            menu.Items.Add(new MenuFlyoutSeparator());
        }
        MenuFlyoutItem explorer = new() { Text = "エクスプローラで開く" };
        explorer.Click += async (_, _) => await Run(async () =>
        {
            StorageFolder folder = await StorageFolder.GetFolderFromPathAsync(entry.IsDirectory ? entry.FullPath : Path.GetDirectoryName(entry.FullPath)!);
            FolderLauncherOptions options = new();
            if (!entry.IsDirectory) options.ItemsToSelect.Add(await StorageFile.GetFileFromPathAsync(entry.FullPath));
            if (!await Launcher.LaunchFolderAsync(folder, options)) throw new InvalidOperationException("エクスプローラを開けませんでした。");
        });
        menu.Items.Add(explorer);
        MenuFlyoutItem delete = new() { Text = "削除" };
        delete.Click += async (_, _) => await Run(() => DeleteWorkspaceEntry(entry));
        menu.Items.Add(delete);
        MenuFlyoutItem rename = new() { Text = "名前を変更" };
        rename.Click += async (_, _) => await Run(() => RenameWorkspaceEntry(entry));
        menu.Items.Add(rename);
        row.ContextFlyout = menu;
        return new TreeViewNode { Content = row, IsExpanded = false, HasUnrealizedChildren = entry.IsDirectory };
    }

    private async void WorkspaceFiles_Expanding(TreeView sender, TreeViewExpandingEventArgs args)
    {
        TreeViewNode node = args.Node;
        if (!node.HasUnrealizedChildren || node.Content is not FrameworkElement { Tag: WorkspaceEntry entry }) return;
        await Run(async () =>
        {
            if (!node.HasUnrealizedChildren) return;
            try
            {
                List<WorkspaceEntry> children = await Task.Run(() => WorkspaceEntry.ReadDirectory(entry.FullPath));
                node.Children.Clear();
                foreach (WorkspaceEntry child in children) node.Children.Add(CreateWorkspaceNode(child));
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
        if (_workspaceCurrentDirectory != null)
            await LoadWorkspaceDirectory(_workspaceCurrentDirectory);
    });

    private async void WorkspaceParent_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        if (_workspaceCurrentDirectory != null && Directory.GetParent(_workspaceCurrentDirectory)is { } parent)
            await LoadWorkspaceDirectory(parent.FullName);
    });
    private async void WorkspaceFiles_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (WorkspaceFiles.SelectedNode?.Content is not FrameworkElement { Tag: WorkspaceEntry entry } || entry.IsDirectory)
            return;
        e.Handled = true;
        await Run(async () =>
        {
            if (!MediaFiles.IsSupported(entry.FullPath))
            {
                await OpenMediaFile(entry.FullPath);
                return;
            }
            await Add(entry.FullPath, _playhead);
            await Refresh();
        });
    }

    private IEnumerable<EditClip> WorkspaceEntryClips(WorkspaceEntry entry) => WorkspaceEntryClips(_project, entry);

    private static IEnumerable<EditClip> WorkspaceEntryClips(EditProject source, WorkspaceEntry entry) => source.Clips.Where(clip =>
        clip.Kind != "subtitle" && (string.Equals(clip.Path, entry.FullPath, StringComparison.OrdinalIgnoreCase) ||
        entry.IsDirectory && clip.Path.StartsWith(Path.TrimEndingDirectorySeparator(entry.FullPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)));

    private async Task DeleteWorkspaceEntry(WorkspaceEntry entry)
    {
        if (WorkspaceEntryClips(entry).Any())
            throw new InvalidOperationException("タイムラインで使用中の素材が含まれています。クリップを削除してから操作してください。");
        ContentDialog dialog = new()
        {
            XamlRoot = Root.XamlRoot, Title = "削除の確認",
            Content = $"「{entry.Name}」を削除しますか？" + (entry.IsDirectory ? "\nフォルダー内のファイルも削除されます。" : ""),
            PrimaryButtonText = "削除", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        // Recheck after the dialog in case the project changed while it was open.
        if (WorkspaceEntryClips(entry).Any()) throw new InvalidOperationException("タイムラインで使用中の素材は削除できません。");
        IStorageItem item = entry.IsDirectory ? await StorageFolder.GetFolderFromPathAsync(entry.FullPath) : await StorageFile.GetFileFromPathAsync(entry.FullPath);
        await item.DeleteAsync(StorageDeleteOption.Default);
        if (_workspaceCurrentDirectory != null) await RefreshWorkspaceNodes(WorkspaceFiles.RootNodes, _workspaceCurrentDirectory);
    }

    private async Task RenameWorkspaceEntry(WorkspaceEntry entry)
    {
        TextBox input = new() { Text = entry.Name, MinWidth = 300 };
        ContentDialog dialog = new()
        {
            XamlRoot = Root.XamlRoot, Title = "名前を変更", Content = input,
            PrimaryButtonText = "変更", CloseButtonText = "キャンセル", DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        string name = input.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ArgumentException("有効なファイル名を入力してください。");
        if (name == entry.Name) return;
        List<EditClip> affected = WorkspaceEntryClips(entry).ToList();
        IStorageItem item = entry.IsDirectory ? await StorageFolder.GetFolderFromPathAsync(entry.FullPath) : await StorageFile.GetFileFromPathAsync(entry.FullPath);
        await item.RenameAsync(name, NameCollisionOption.FailIfExists);
        foreach (EditClip clip in affected.Concat(_undo.Concat(_redo).SelectMany(snapshot => WorkspaceEntryClips(snapshot, entry))))
            clip.Path = item.Path + clip.Path[entry.FullPath.Length..];
        if (_workspaceCurrentDirectory != null) await RefreshWorkspaceNodes(WorkspaceFiles.RootNodes, _workspaceCurrentDirectory);
        if (affected.Count > 0) { Draw(); await Refresh(); }
    }

    private static async Task OpenMediaFile(string path)
    {
        StorageFile file = await StorageFile.GetFileFromPathAsync(path);
        if (!await Launcher.LaunchFileAsync(file))
            throw new InvalidOperationException("この素材を開く標準アプリが見つかりませんでした。");
    }
}


