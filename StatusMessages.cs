using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;

public sealed partial class MainWindow
{
    private readonly DispatcherTimer statusMessageTimer = new()
    {
        Interval = TimeSpan.FromSeconds(3)
    };
    private long statusMessageSubscription;

    private void InitializeStatusMessages()
    {
        statusMessageTimer.Tick += (_, _) =>
        {
            statusMessageTimer.Stop();
            Status.Text = "";
        };

        statusMessageSubscription = Status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            statusMessageTimer.Stop();
            if (!string.IsNullOrWhiteSpace(Status.Text))
                statusMessageTimer.Start();
        });
    }

    private void DisposeStatusMessages()
    {
        statusMessageTimer.Stop();
        Status.UnregisterPropertyChangedCallback(TextBlock.TextProperty, statusMessageSubscription);
    }
}
