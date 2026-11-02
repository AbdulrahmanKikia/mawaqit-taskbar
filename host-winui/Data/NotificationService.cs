using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Mawaqit.WinUI.Data;

internal static class NotificationService
{
    /// <summary>Must match AppUserModelID on the installer's Start-menu shortcut.</summary>
    public const string AppId = "Mawaqit.Widget";
    private const string RegistryKey = @"Software\Classes\AppUserModelId\" + AppId;

    public static event Action? ShowWidgetRequested;

    private static ToastNotifier? _notifier;

    /// <summary>
    /// Unpackaged apps only get toasts when Windows knows their AppUserModelID. We register
    /// it per user (display name + icon) and tag this process with it — the same approach as
    /// CommunityToolkit's ToastNotificationManagerCompat. Without this, Show() silently drops
    /// every toast.
    /// </summary>
    public static void Init()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RegistryKey))
            {
                key.SetValue("DisplayName", "Mawaqit Widget");
                var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");
                if (File.Exists(icon)) key.SetValue("IconUri", icon);
            }
            var hr = SetCurrentProcessExplicitAppUserModelID(AppId);
            if (hr != 0) Log.Warning("SetCurrentProcessExplicitAppUserModelID failed: 0x{Hr:X8}", hr);
            _notifier = ToastNotificationManager.CreateToastNotifier(AppId);
            Log.Information("Notifications ready ({Setting})", SettingOf(_notifier)?.ToString() ?? "unknown until the Start-menu shortcut exists");
        }
        catch (Exception ex)
        {
            _notifier = null;
            Log.Warning(ex, "Toast notifications unavailable; using tray balloons");
        }
    }

    public static void Unregister() { }

    public static void Show(string title, string body)
    {
        // Setting is unknown (throws) for a registry-only registration; toasts still show then.
        if (_notifier is { } notifier && SettingOf(notifier) is null or NotificationSetting.Enabled)
        {
            try
            {
                var xml = $"""
                    <toast>
                      <visual>
                        <binding template="ToastGeneric">
                          <text>{Esc(title)}</text>
                          <text>{Esc(body)}</text>
                        </binding>
                      </visual>
                    </toast>
                    """;
                var doc = new XmlDocument();
                doc.LoadXml(xml);
                notifier.Show(new ToastNotification(doc));
                return;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Toast failed; tray balloon instead");
            }
        }
        else
        {
            Log.Information("Toasts are {Setting}; tray balloon instead",
                _notifier is { } n ? SettingOf(n)?.ToString() : "unavailable");
        }
        TaskBarManager.Balloon(title, body);
    }

    public static void SendWidgetStillRunningToast()
        => Show("Mawaqit still running", "Click the tray icon near the clock to show the widget again.");

    public static void RequestShowWidget() => ShowWidgetRequested?.Invoke();

    private static NotificationSetting? SettingOf(ToastNotifier notifier)
    {
        try
        {
            return notifier.Setting;
        }
        catch (COMException ex)
        {
            // 0x80070490: Windows has no shortcut-based record of the app yet.
            Log.Debug(ex, "Toast setting not available");
            return null;
        }
    }

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
