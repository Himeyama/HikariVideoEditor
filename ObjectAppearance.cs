using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VideoEditor;

public static class ObjectAppearance
{
    public static Color Color(EditClip clip) => clip.Kind == "audio"
        ? Windows.UI.Color.FromArgb(255, 192, 40, 40)
        : Windows.UI.Color.FromArgb(255, 40, 80, 208);
    public static SolidColorBrush Brush(EditClip clip) => new(Color(clip));
}
