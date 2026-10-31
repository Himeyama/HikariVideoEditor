using Microsoft.UI.Input;
using Microsoft.UI.Xaml.Controls;

namespace VideoEditor;

public sealed class PreviewInteractionCanvas : Canvas
{
    private readonly Dictionary<InputSystemCursorShape, InputSystemCursor> _cursors = new();
    public void SetCursor(InputSystemCursorShape shape)
    {
        if (!_cursors.TryGetValue(shape, out InputSystemCursor? cursor))
            _cursors[shape] = cursor = InputSystemCursor.Create(shape);
        ProtectedCursor = cursor;
    }
}
