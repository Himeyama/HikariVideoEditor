using System.Runtime.InteropServices;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace VideoEditor;
public sealed partial class MainWindow
{
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);
    private void ConfigureWindow()
    {
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "VideoEditor.ico"));
        // XAML sizes are DIPs; AppWindow sizes and caption insets are physical pixels.
        uint dpi = GetDpiForWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
        double scale = dpi == 0 ? 1 : dpi / 96.0;
        Windows.Graphics.RectInt32 area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Resize(new Windows.Graphics.SizeInt32(Math.Min((int)Math.Round(1500 * scale), area.Width), Math.Min((int)Math.Round(980 * scale), area.Height)));
        ExtendsContentIntoTitleBar = true;
        SystemBackdrop = new MicaBackdrop();
        SetTitleBar(TitleDragRegion);
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
        AppWindowTitleBar bar = AppWindow.TitleBar;
        bar.ButtonBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        bar.ButtonInactiveBackgroundColor = Color.FromArgb(0, 0, 0, 0);
        Root.ActualThemeChanged += (_, _) => UpdateCaptionTheme();
        UpdateCaptionTheme();
        Root.Loaded += (_, _) =>
        {
            Root.XamlRoot.Changed += XamlRoot_Changed;
            UpdateTitleBarInsets();
            ApplyPanelLayout(_preferredLayout);
        };
        Workspace.Loaded += (_, _) => ApplyPanelLayout(_preferredLayout);
        AppWindow.Changed += (_, _) => UpdateTitleBarInsets();
    }

    private void UpdateCaptionTheme()
    {
        // Native caption buttons share the XAML theme; hover/pressed states remain system defaults.
        AppWindow.TitleBar.ButtonForegroundColor = Root.ActualTheme == ElementTheme.Dark ? Microsoft.UI.Colors.White : Microsoft.UI.Colors.Black;
    }

    private void XamlRoot_Changed(XamlRoot sender, XamlRootChangedEventArgs args) => UpdateTitleBarInsets();
    private void TitleBar_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateTitleBarInsets();
    private void UpdateTitleBarInsets()
    {
        if (CustomTitleBar == null || Root.XamlRoot == null)
            return;
        double scale = Root.XamlRoot.RasterizationScale * _preferences.Scale;
        TitleLeftInset.Width = new GridLength(AppWindow.TitleBar.LeftInset / scale);
        TitleRightInset.Width = new GridLength(AppWindow.TitleBar.RightInset / scale);
    }
}


