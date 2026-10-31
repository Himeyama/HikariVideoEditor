using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private string? _savedProjectSnapshot;
    private string? _savedProjectPath;
    private string ProjectSnapshot() => JsonSerializer.Serialize(_project);

    private async Task AutoSaveChanges(string previous)
    {
        if (!_preferences.AutoSaveEnabled || string.IsNullOrWhiteSpace(_project.WorkspaceDirectory) || previous == ProjectSnapshot())
            return;
        try
        {
            string directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(_project.WorkspaceDirectory));
            string name = Path.GetFileName(directory);
            if (string.IsNullOrEmpty(name)) name = "Project";
            await Save(Path.Combine(directory, name + ".hve"));
        }
        catch (Exception exception)
        {
            Status.Text = $"自動保存に失敗しました: {exception.Message}";
        }
    }

    private async Task Save(string path)
    {
        string full = Path.GetFullPath(path);
        string temp = full + ".tmp";
        string snapshot = ProjectSnapshot();
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(_project, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, full, true);
        _savedProjectSnapshot = snapshot;
        _savedProjectPath = full;
        Status.Text = $"保存しました: {full}";
    }

    private async Task Open(string path)
    {
        EditProject next = JsonSerializer.Deserialize<EditProject>(await File.ReadAllTextAsync(path)) ?? throw new ArgumentException("無効なプロジェクトです。");
        await CompositionBuilder.Build(next);
        Remember();
        _project = next;
        _selectedIds.Clear();
        _selected = null;
        Draw();
        await LoadWorkspaceDirectory(_project.WorkspaceDirectory);
        await Refresh();
        _savedProjectSnapshot = ProjectSnapshot();
        _savedProjectPath = Path.GetFullPath(path);
        if (!string.IsNullOrWhiteSpace(_project.WorkspaceDirectory))
        {
            _preferences.RememberWorkspace(_project.WorkspaceDirectory);
            UpdateRecentWorkspacesMenu();
            _preferences.Save(AppPreferences.DefaultPath);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        FileSavePicker picker = new()
        {
            SuggestedFileName = "Project"
        };
        InitPicker(picker);
        picker.FileTypeChoices.Add("Hikari Project", new[] { ".hve" });
        StorageFile file = await picker.PickSaveFileAsync();
        if (file != null)
            await Save(file.Path);
    });
    private async void Open_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        FileOpenPicker picker = new();
        InitPicker(picker);
        picker.FileTypeFilter.Add(".hve");
        StorageFile file = await picker.PickSingleFileAsync();
        if (file != null)
            await Open(file.Path);
    });
}

