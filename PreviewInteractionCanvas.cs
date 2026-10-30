using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;

public sealed class PreviewInteractionCanvas : Canvas
{
    private readonly Dictionary<InputSystemCursorShape, InputSystemCursor> cursors = new();
    public void SetCursor(InputSystemCursorShape shape)
    {
        if (!cursors.TryGetValue(shape, out var cursor))
            cursors[shape] = cursor = InputSystemCursor.Create(shape);
        ProtectedCursor = cursor;
    }
}
