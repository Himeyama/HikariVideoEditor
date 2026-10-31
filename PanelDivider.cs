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
    private uint? _pointerId;
    private Point _previousPoint;
    public PanelDivider()
    {
        IsTabStop = false;
        Loaded += (_, _) => ProtectedCursor = InputSystemCursor.Create(IsHorizontal ? InputSystemCursorShape.SizeNorthSouth : InputSystemCursorShape.SizeWestEast);
        PointerPressed += (_, args) =>
        {
            PointerPoint pointer = args.GetCurrentPoint(null);
            if (_pointerId != null || !pointer.Properties.IsLeftButtonPressed || !CapturePointer(args.Pointer))
                return;
            _pointerId = args.Pointer.PointerId;
            _previousPoint = pointer.Position;
            DragStarted?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        };
        PointerMoved += (_, args) =>
        {
            if (_pointerId != args.Pointer.PointerId)
                return;
            Point point = args.GetCurrentPoint(null).Position;
            DragDelta?.Invoke(this, new PanelDragDeltaEventArgs(point.X - _previousPoint.X, point.Y - _previousPoint.Y));
            _previousPoint = point;
            args.Handled = true;
        };
        PointerReleased += (_, args) => FinishDrag(args.Pointer.PointerId);
        PointerCanceled += (_, args) => FinishDrag(args.Pointer.PointerId);
        PointerCaptureLost += (_, args) => FinishDrag(args.Pointer.PointerId);
    }

    private void FinishDrag(uint id)
    {
        if (_pointerId != id)
            return;
        _pointerId = null;
        ReleasePointerCaptures();
        DragCompleted?.Invoke(this, EventArgs.Empty);
    }
}
