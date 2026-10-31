using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;
public sealed class TimelineClipView : Grid
{
    private static readonly InputSystemCursor _arrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    private static readonly InputSystemCursor _resize = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    private bool _resizing;
    public TimelineClipView()
    {
        PointerEntered += (_, e) => UpdateCursor(e.GetCurrentPoint(this).Position.X);
        PointerMoved += (_, e) => UpdateCursor(e.GetCurrentPoint(this).Position.X);
        PointerExited += (_, _) =>
        {
            if (!_resizing)
                ProtectedCursor = _arrow;
        };
    }

    public void SetResizeActive(bool value)
    {
        _resizing = value;
        ProtectedCursor = value ? _resize : _arrow;
    }

    private void UpdateCursor(double x) => ProtectedCursor = _resizing || x < 8 || x > ActualWidth - 8 ? _resize : _arrow;
}
