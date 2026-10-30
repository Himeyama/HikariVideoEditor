using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private void InitializePages()
    {
        Root.Children.Remove(Workspace);
        Root.Children.Remove(SettingsPage);
        MainFrame.Navigated += (_, args) =>
        {
            // Pass UI content locally, without marshaling controls as navigation parameters.
            ((Page)args.Content).Content = args.SourcePageType == typeof(EditorContentPage) ? Workspace : SettingsPage;
        };
        MainFrame.Navigate(typeof(EditorContentPage), null, new SuppressNavigationTransitionInfo());
        MainFrame.BackStack.Clear();
    }

    private void NavigateToContent(Type page, UIElement content)
    {
        if (MainFrame.SourcePageType == page) return;
        MainFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        MainFrame.BackStack.Clear();
    }
}
