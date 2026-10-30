using System.Text.Json;
using Microsoft.UI.Xaml;
using Windows.Media.Editing;
using Windows.Media.Transcoding;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VideoEditor;
public sealed partial class MainWindow
{
    private async Task Save(string path)
    {
        var full = Path.GetFullPath(path);
        var temp = full + ".tmp";
        await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(project, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, full, true);
        Status.Text = $"保存しました: {full}";
    }

    private async Task Open(string path)
    {
        var next = JsonSerializer.Deserialize<EditProject>(await File.ReadAllTextAsync(path)) ?? throw new ArgumentException("無効なプロジェクトです。");
        await CompositionBuilder.Build(next);
        Remember();
        project = next;
        selectedIds.Clear();
        selected = null;
        Draw();
        await LoadWorkspaceDirectory(project.WorkspaceDirectory);
        await Refresh();
        if (!string.IsNullOrWhiteSpace(project.WorkspaceDirectory))
        {
            preferences.RememberWorkspace(project.WorkspaceDirectory);
            UpdateRecentWorkspacesMenu();
            preferences.Save(AppPreferences.DefaultPath);
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var picker = new FileSavePicker
        {
            SuggestedFileName = "Project"
        };
        InitPicker(picker);
        picker.FileTypeChoices.Add("Hikari Project", new[] { ".hve" });
        var file = await picker.PickSaveFileAsync();
        if (file != null)
            await Save(file.Path);
    });
    private async void Open_Click(object sender, RoutedEventArgs e) => await Run(async () =>
    {
        var picker = new FileOpenPicker();
        InitPicker(picker);
        picker.FileTypeFilter.Add(".hve");
        var file = await picker.PickSingleFileAsync();
        if (file != null)
            await Open(file.Path);
    });
}

