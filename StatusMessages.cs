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
    private const string StatusSpinnerFrames = "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
    private readonly DispatcherTimer _statusSpinnerTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private int _statusSpinnerFrame;
    private int _pendingStatusOperations;

    private IDisposable BeginStatusOperation()
    {
        if (_pendingStatusOperations++ == 0 && !_closed)
        {
            _statusMessageTimer.Stop();
            Status.Text = "処理中…";
            _statusSpinnerFrame = 0;
            StatusSpinner.Text = StatusSpinnerFrames[0].ToString();
            StatusSpinner.Visibility = Visibility.Visible;
            _statusSpinnerTimer.Start();
        }
        return new StatusOperation(this);
    }

    private void EndStatusOperation()
    {
        if (--_pendingStatusOperations != 0 || _closed)
            return;
        _statusSpinnerTimer.Stop();
        StatusSpinner.Visibility = Visibility.Collapsed;
        if (Status.Text == "処理中…")
            Status.Text = "";
        if (!string.IsNullOrWhiteSpace(Status.Text))
            _statusMessageTimer.Start();
    }

    private sealed class StatusOperation(MainWindow owner) : IDisposable
    {
        private MainWindow? _owner = owner;

        public void Dispose()
        {
            _owner?.EndStatusOperation();
            _owner = null;
        }
    }

    private void InitializeStatusMessages()
    {
        _statusSpinnerTimer.Tick += (_, _) =>
        {
            _statusSpinnerFrame = (_statusSpinnerFrame + 1) % StatusSpinnerFrames.Length;
            StatusSpinner.Text = StatusSpinnerFrames[_statusSpinnerFrame].ToString();
        };
        _statusMessageTimer.Tick += (_, _) =>
        {
            _statusMessageTimer.Stop();
            if (_closePending)
                return;
            Status.Text = "";
        };

        _statusMessageSubscription = Status.RegisterPropertyChangedCallback(TextBlock.TextProperty, (_, _) =>
        {
            _statusMessageTimer.Stop();
            if (_pendingStatusOperations == 0 && !string.IsNullOrWhiteSpace(Status.Text))
                _statusMessageTimer.Start();
        });
    }

    private void DisposeStatusMessages()
    {
        _statusMessageTimer.Stop();
        _statusSpinnerTimer.Stop();
        Status.UnregisterPropertyChangedCallback(TextBlock.TextProperty, _statusMessageSubscription);
    }
}
