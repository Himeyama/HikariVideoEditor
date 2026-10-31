using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace VideoEditor;

public sealed class MenuBarAction : MenuBarItem
{
    private Button? _contentButton;

    public event RoutedEventHandler? Click;

    protected override void OnApplyTemplate()
    {
        if (_contentButton != null)
            _contentButton.Click -= ContentButton_Click;
        base.OnApplyTemplate();
        _contentButton = GetTemplateChild("ContentButton") as Button;
        if (_contentButton != null)
        {
            _contentButton.Content = new FontIcon
            {
                Glyph = Title,
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 16
            };
            _contentButton.ContextFlyout = null;
            _contentButton.Click += ContentButton_Click;
        }
    }

    private void ContentButton_Click(object sender, RoutedEventArgs e) => Click?.Invoke(this, e);
}
