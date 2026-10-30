using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace VideoEditor;
public sealed class PanelDragDeltaEventArgs(double horizontalChange, double verticalChange) : EventArgs
{
    public double HorizontalChange { get; } = horizontalChange;
    public double VerticalChange { get; } = verticalChange;
}

/// <summary>Reports window-relative pointer movement while the divider is captured.</summary>
public sealed class PanelDivider : Control
{
    public bool IsHorizontal { get; set; }

    public event EventHandler? DragStarted;
    public event EventHandler<PanelDragDeltaEventArgs>? DragDelta;
    public event EventHandler? DragCompleted;
    private uint? pointerId;
    private Point previousPoint;
    public PanelDivider()
    {
        IsTabStop = false;
        Loaded += (_, _) => ProtectedCursor = InputSystemCursor.Create(IsHorizontal ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast);
        PointerPressed += (_, args) =>
        {
            var pointer = args.GetCurrentPoint(null);
            if (pointerId != null || !pointer.Properties.IsLeftButtonPressed || !CapturePointer(args.Pointer))
                return;
            pointerId = args.Pointer.PointerId;
            previousPoint = pointer.Position;
            DragStarted?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        };
        PointerMoved += (_, args) =>
        {
            if (pointerId != args.Pointer.PointerId)
                return;
            var point = args.GetCurrentPoint(null).Position;
            DragDelta?.Invoke(this, new PanelDragDeltaEventArgs(point.X - previousPoint.X, point.Y - previousPoint.Y));
            previousPoint = point;
            args.Handled = true;
        };
        PointerReleased += (_, args) => FinishDrag(args.Pointer.PointerId);
        PointerCanceled += (_, args) => FinishDrag(args.Pointer.PointerId);
        PointerCaptureLost += (_, args) => FinishDrag(args.Pointer.PointerId);
    }

    private void FinishDrag(uint id)
    {
        if (pointerId != id)
            return;
        pointerId = null;
        ReleasePointerCaptures();
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
