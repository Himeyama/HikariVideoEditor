using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace VideoEditor;
/// <summary>A local folder browser built with WinUI controls, without an OS picker.</summary>
public sealed partial class FolderBrowser : UserControl
{
    public string? CurrentDirectory { get; private set; }

    private int navigationVersion;
    public async Task<bool> ConfirmSelection()
    {
        string requested = Address.Text;
        if (string.Equals(requested, CurrentDirectory, StringComparison.OrdinalIgnoreCase) && Folders.SelectedItem is WorkspaceEntry selectedFolder)
        {
            requested = selectedFolder.FullPath;
        }

        await Navigate(requested);
        return string.IsNullOrEmpty(ErrorText.Text) && CurrentDirectory != null;
    }

    public FolderBrowser()
    {
        InitializeComponent();
    }

    public async Task Navigate(string path)
    {
        int version = ++navigationVersion;
        try
        {
            string fullPath = Path.GetFullPath(path);
            var entries = await Task.Run(() => WorkspaceEntry.ReadDirectory(fullPath, foldersOnly: true));
            if (version != navigationVersion)
                return;
            CurrentDirectory = fullPath;
            Address.Text = fullPath;
            Folders.ItemsSource = entries;
            ErrorText.Text = "";
        }
        catch (Exception exception)when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            if (version == navigationVersion)
                ErrorText.Text = exception.Message;
        }
    }

    private async void Home_Click(object sender, RoutedEventArgs e) => await Navigate(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));

    private async void Parent_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentDirectory != null && Directory.GetParent(CurrentDirectory)is { } parent)
            await Navigate(parent.FullName);
    }

    private async void Navigate_Click(object sender, RoutedEventArgs e) => await Navigate(Address.Text);
    private async void Address_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != Windows.System.VirtualKey.Enter)
            return;
        e.Handled = true;
        await Navigate(Address.Text);
    }

    private async void Folders_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (Folders.SelectedItem is WorkspaceEntry entry)
            await Navigate(entry.FullPath);
    }
}

