using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer _statusMessageTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3)
    };
    private long _statusMessageSubscription;

    private void InitializeStatusMessages()
    {
        _statusMessageTimer.Tick += (_, _) =>
        {
            _statusMessageTimer.Stop();
            Status.Text = "";
        };

        _statusMessageSubscription = Status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            _statusMessageTimer.Stop();
            if (!string.IsNullOrWhiteSpace(Status.Text))
                _statusMessageTimer.Start();
        });
    }

    private void DisposeStatusMessages()
    {
        _statusMessageTimer.Stop();
        Status.UnregisterPropertyChangedCallback(TextBlock.TextProperty, _statusMessageSubscription);
    }
}
