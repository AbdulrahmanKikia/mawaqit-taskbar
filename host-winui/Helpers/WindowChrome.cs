using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;

namespace Mawaqit.WinUI.Helpers;

internal static class WindowChrome
{
    public static void Apply(Window window, UIElement titleBar, int width, int height)
    {
        window.SystemBackdrop = new MicaBackdrop();
        window.ExtendsContentIntoTitleBar = true;
        window.SetTitleBar(titleBar);
        ApplyTheme(window);

        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(id);
        appWindow.Resize(new SizeInt32(width, height));
        try
        {
            appWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "icon.ico"));
        }
        catch { /* unpackaged icon is optional */ }
    }

    public static void ApplyTheme(Window window)
    {
        if (window.Content is FrameworkElement root)
            root.RequestedTheme = ThemeHelper.GeneralTheme;
    }
}
