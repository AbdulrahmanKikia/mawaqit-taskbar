using Microsoft.Win32;

namespace Mawaqit.WinUI.Data;

internal static class Autostart
{
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "Mawaqit Widget";

    public static void Apply(bool enabled)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
        if (key is null) return;
        if (enabled)
        {
            var exe = Environment.ProcessPath ?? "";
            key.SetValue(ValueName, $"\"{exe}\"");
        }
        else if (key.GetValue(ValueName) != null)
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}
