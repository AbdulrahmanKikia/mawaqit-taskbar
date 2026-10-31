using Serilog;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace Mawaqit.WinUI.Data;

internal static class NotificationService
{
    public static event Action? ShowWidgetRequested;

    public static void Init()
    {
        try
        {
            // Unpackaged toasts work when a shortcut with an AppUserModelID exists;
            // balloon via tray is the reliable fallback.
            Log.Information("Notification service ready");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not initialize notifications");
        }
    }

    public static void Unregister() { }

    public static void Show(string title, string body)
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
            ToastNotificationManager.CreateToastNotifier("Mawaqit.Widget").Show(new ToastNotification(doc));
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "toast failed; tray balloon instead");
            TaskBarManager.Balloon(title, body);
        }
    }

    public static void SendWidgetStillRunningToast()
        => Show("Mawaqit still running", "Click the tray icon near the clock to show the widget again.");

    public static void RequestShowWidget() => ShowWidgetRequested?.Invoke();

    private static string Esc(string s) => System.Security.SecurityElement.Escape(s) ?? s;
}
