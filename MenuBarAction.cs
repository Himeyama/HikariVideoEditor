using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VideoEditor;

public sealed class MenuBarAction : MenuBarItem
{
    private Button? contentButton;

    public event RoutedEventHandler? Click;

    protected override void OnApplyTemplate()
    {
        if (contentButton != null)
            contentButton.Click -= ContentButton_Click;
        base.OnApplyTemplate();
        contentButton = GetTemplateChild("ContentButton") as Button;
        if (contentButton != null)
        {
            contentButton.Content = new FontIcon
            {
                Glyph = Title,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16
            };
            contentButton.ContextFlyout = null;
            contentButton.Click += ContentButton_Click;
        }
    }

    private void ContentButton_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);
}
