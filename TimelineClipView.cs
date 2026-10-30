using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;
public sealed class TimelineClipView : Grid
{
    private static readonly InputSystemCursor Arrow = InputSystemCursor.Create(InputSystemCursorShape.Arrow);
    private static readonly InputSystemCursor Resize = InputSystemCursor.Create(InputSystemCursorShape.SizeWestEast);
    private bool resizing;
    public TimelineClipView()
    {
        PointerEntered += (_, e) => UpdateCursor(e.GetCurrentPoint(this).Position.X);
        PointerMoved += (_, e) => UpdateCursor(e.GetCurrentPoint(this).Position.X);
        PointerExited += (_, _) =>
        {
            if (!resizing)
                ProtectedCursor = Arrow;
        };
    }

    public void SetResizeActive(bool value)
    {
        resizing = value;
        ProtectedCursor = value ? Resize : Arrow;
    }

    private void UpdateCursor(double x) => ProtectedCursor = resizing || x < 8 || x > ActualWidth - 8 ? Resize : Arrow;
}
