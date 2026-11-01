using Microsoft.Win32;
using Serilog;
using System.Management;
using System.Security.Principal;

namespace Mawaqit.WinUI.Helpers;

internal static class ThemeWatcher
{
    private const string RegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly string[] Watched = ["AppsUseLightTheme", "SystemUsesLightTheme", "ColorPrevalence"];
    private static readonly Dictionary<string, object?> Current = new();
    private static ManagementEventWatcher? _watcher;
    private static bool _started;

    public static void Ensure()
    {
        if (_started) return;
        _started = true;
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key != null)
                {
                    foreach (var name in Watched)
                        Current[name] = key.GetValue(name, 0);
                }
            }

            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (sid is null) return;

            var query = new WqlEventQuery(
                "SELECT * FROM RegistryKeyChangeEvent WHERE " +
                "Hive = 'HKEY_USERS' " +
                @"AND KeyPath = '" + sid + @"\\Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize'");
            query.WithinInterval = TimeSpan.FromSeconds(1);

            _watcher = new ManagementEventWatcher(query);
            _watcher.EventArrived += (_, _) => OnChanged();
            _watcher.Start();
            App.Quitting += () => _watcher?.Dispose();
            Log.Information("Theme watcher started");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Theme watcher failed to start");
        }
    }

    private static void OnChanged()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
            if (key is null) return;
            bool changed = false;
            foreach (var name in Watched)
            {
                var value = key.GetValue(name, 0);
                if (!Equals(Current.GetValueOrDefault(name), value))
                {
                    Current[name] = value;
                    changed = true;
                }
            }
            if (changed) ThemeHelper.Notify();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Theme watcher read failed");
        }
    }
}
