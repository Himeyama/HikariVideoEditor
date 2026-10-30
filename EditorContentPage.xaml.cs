using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace VideoEditor;

public sealed partial class EditorContentPage : Page
{
    public EditorContentPage() => InitializeComponent();

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        Content = null;
        base.OnNavigatedFrom(e);
    }
}
