using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Mawaqit.WinUI.Interop;
using Mawaqit.WinUI.Views;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Serilog;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using Windows.Graphics;

namespace Mawaqit.WinUI;

/// <summary>0.1.x-style docked AppBar: full-width strip that reserves desktop work area.</summary>
internal class AppBarWidget : IWidgetHost
{
    private const int BarHeightDip = 40;
    private readonly string WidgetClassName = "MawaqitAppBarWinRT";
    private readonly string displayPath;
    private readonly double dpiScale;

    private IntPtr hwnd;
    private AppWindow appWindow;
    private StripBar stripBar;
    private DesktopWindowXamlSource host;
    private WndProc wndProc;
    private bool registered;
    private bool initialized;
    private bool destroyed;
    private bool disposedValue;
    private SolidColorBrush? _fill;

    public IntPtr Handle => hwnd != IntPtr.Zero ? hwnd : throw new InvalidOperationException("The strip is not initialized.");
    public string DisplayPath => displayPath;
    public WidgetHostCapabilities Capabilities => WidgetHostCapabilities.None;
    public DisplayEntity Display => DisplayHelper.AvailableDisplays.SingleOrDefault(d => d.Display.DevicePath == displayPath);

    public event EventHandler Destroying;

    public AppBarWidget(DisplayEntity display)
    {
        displayPath = display.Display.DevicePath;
        dpiScale = display.Dpi / 96d;
    }

    public void Initialize()
    {
        Log.Information("Initializing AppBar strip host");
        host = new DesktopWindowXamlSource();
        hwnd = CreateHostWindow();
        var id = Win32Interop.GetWindowIdFromWindow(hwnd);
        appWindow = AppWindow.GetFromWindowId(id);
        appWindow.IsShownInSwitchers = false;
        appWindow.Destroying += AppWindow_Destroying;

        host.Initialize(id);
        host.SiteBridge.ResizePolicy = Microsoft.UI.Content.ContentSizePolicy.ResizeContentToParentWindow;
        stripBar = new StripBar();
        stripBar.DisplayModeChanged += _ => ApplyChipSize(AppState.Current.Config.ChipSize);
        var fill = new SolidColorBrush(StripFillColor());
        _fill = fill;
        stripBar.Background = fill;
        host.Content = new Grid
        {
            Background = fill,
            HorizontalAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
            VerticalAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            Children = { stripBar }
        };
        ThemeHelper.ThemeChanged += ApplyStripTheme;

        RegisterAppBar();
        PositionAppBar();
        initialized = true;
        Log.Information("AppBar strip initialization done");
    }

    public void Show() => appWindow.Show(false);

    public void Destroy()
    {
        RemoveAppBar();
        appWindow?.Destroy();
    }

    public void UpdatePosition(bool force = false)
    {
        if (initialized && !destroyed) PositionAppBar();
    }

    public void ApplyChipSize(ChipSize size) => stripBar?.ApplyDensity();

    public void InvalidateElementsAlignment(bool autoAlign)
    {
        if (stripBar != null) stripBar.ElementsAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Left;
    }

    public void StartDragging() { /* full-width AppBar is not dragged */ }

    private void RegisterAppBar()
    {
        var abd = BarData();
        registered = Shell32.SHAppBarMessage(AppBarMessage.ABM_NEW, ref abd) != UIntPtr.Zero;
        Log.Information("ABM_NEW result={Registered}", registered);
    }

    private void PositionAppBar()
    {
        int h = Math.Max(32, (int)Math.Ceiling(BarHeightDip * dpiScale));
        ScreenRect(out int left, out int top, out int right, out int bottom);
        var abd = BarData();
        abd.rc = new RECT { left = left, top = bottom - h, right = right, bottom = bottom };
        Shell32.SHAppBarMessage(AppBarMessage.ABM_QUERYPOS, ref abd);
        abd.rc.top = abd.rc.bottom - h;
        Shell32.SHAppBarMessage(AppBarMessage.ABM_SETPOS, ref abd);
        int w = abd.rc.right - abd.rc.left;
        int hh = abd.rc.bottom - abd.rc.top;
        User32.MoveWindow(hwnd, abd.rc.left, abd.rc.top, w, hh, true);
        appWindow?.MoveAndResize(new RectInt32(abd.rc.left, abd.rc.top, w, hh));
        User32.SetWindowPos(hwnd, User32.HWND_TOPMOST, 0, 0, 0, 0, SWP.SWP_NOMOVE | SWP.SWP_NOSIZE | SWP.SWP_NOACTIVATE);
    }

    private void RemoveAppBar()
    {
        if (!registered || hwnd == IntPtr.Zero) return;
        var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), hWnd = hwnd };
        Shell32.SHAppBarMessage(AppBarMessage.ABM_REMOVE, ref abd);
        registered = false;
    }

    private APPBARDATA BarData() => new()
    {
        cbSize = Marshal.SizeOf<APPBARDATA>(),
        hWnd = hwnd,
        uCallbackMessage = (int)WindowMessage.WM_APPBAR,
        uEdge = (int)AppBarEdge.Bottom
    };

    private void ScreenRect(out int left, out int top, out int right, out int bottom)
    {
        var display = Display;
        if (display != null)
        {
            var s = display.CachedSettings;
            left = s.Position.X;
            top = s.Position.Y;
            right = s.Position.X + s.Resolution.Width;
            bottom = s.Position.Y + s.Resolution.Height;
            return;
        }
        left = 0;
        top = 0;
        right = User32.GetSystemMetrics(SystemMetric.SM_CXSCREEN);
        bottom = User32.GetSystemMetrics(SystemMetric.SM_CYSCREEN);
    }

    private void AppWindow_Destroying(AppWindow sender, object args)
    {
        appWindow.Destroying -= AppWindow_Destroying;
        RemoveAppBar();
        Destroying?.Invoke(this, EventArgs.Empty);
        destroyed = true;
    }

    private void RegisterWindowClass()
    {
        wndProc = WindowProc;
        var wndClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            hInstance = Kernel32.GetModuleHandle(null),
            lpfnWndProc = wndProc,
            lpszClassName = WidgetClassName,
            hbrBackground = Gdi32.CreateSolidBrush(StripFillColorRef())
        };
        User32.RegisterClassEx(ref wndClass);
    }

    private IntPtr CreateHostWindow()
    {
        RegisterWindowClass();
        return User32.CreateWindowEx(
            dwExStyle: WindowStylesExtended.WS_EX_TOOLWINDOW | WindowStylesExtended.WS_EX_TOPMOST,
            lpClassName: WidgetClassName,
            lpWindowName: "Mawaqit Strip",
            dwStyle: WindowStyles.WS_POPUP | WindowStyles.WS_VISIBLE,
            x: 0, y: 0, nWidth: 0, nHeight: 0,
            hWndParent: IntPtr.Zero,
            hMenu: IntPtr.Zero,
            hInstance: IntPtr.Zero,
            lpParam: IntPtr.Zero);
    }

    private void ApplyStripTheme()
    {
        void Set()
        {
            var c = StripFillColor();
            if (_fill != null) _fill.Color = c;
            if (stripBar != null) stripBar.Background = _fill;
            if (host?.Content is Grid g) g.Background = _fill;
        }
        var q = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        if (q is null || q.HasThreadAccess) Set();
        else q.TryEnqueue(Set);
    }

    internal static Windows.UI.Color StripFillColor()
    {
        if (SystemInfos.IsAccentColorOnTaskBar() == true)
        {
            var (r, g, b, a) = SystemInfos.GetAccentColor();
            if (a > 0 && (r | g | b) != 0)
                return Windows.UI.Color.FromArgb(255, r, g, b);
        }
        return SystemInfos.IsLightThemeUsed() == true
            ? Windows.UI.Color.FromArgb(255, 243, 243, 243)
            : Windows.UI.Color.FromArgb(255, 32, 32, 32);
    }

    private static uint StripFillColorRef()
    {
        var c = StripFillColor();
        return (uint)(c.R | (c.G << 8) | (c.B << 16));
    }

    private IntPtr WindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        var msg = (WindowMessage)uMsg;
        if (msg == WindowMessage.WM_ACTIVATE)
        {
            var abd = BarData();
            Shell32.SHAppBarMessage(AppBarMessage.ABM_ACTIVATE, ref abd);
        }
        else if (msg == WindowMessage.WM_WINDOWPOSCHANGED)
        {
            var abd = BarData();
            Shell32.SHAppBarMessage(AppBarMessage.ABM_WINDOWPOSCHANGED, ref abd);
        }
        else if (msg == WindowMessage.WM_DISPLAYCHANGE)
        {
            if (initialized && !destroyed) PositionAppBar();
        }
        else if (uMsg == (uint)WindowMessage.WM_APPBAR && wParam.ToInt32() == AppBarNotify.ABN_POSCHANGED)
        {
            PositionAppBar();
        }
        return User32.DefWindowProc(hWnd, uMsg, wParam, lParam);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposedValue) return;
        if (disposing)
        {
            ThemeHelper.ThemeChanged -= ApplyStripTheme;
            RemoveAppBar();
            if (!destroyed) appWindow?.Destroy();
            host?.Dispose();
        }
        User32.UnregisterClass(WidgetClassName, Kernel32.GetModuleHandle(null));
        disposedValue = true;
    }

    ~AppBarWidget() => Dispose(false);

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
