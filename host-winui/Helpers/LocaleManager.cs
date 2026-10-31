using System.Globalization;

namespace Mawaqit.WinUI.Helpers;

internal sealed class LocaleManager
{
    public static LocaleManager Default { get; } = new();

    public event EventHandler? CurrentChanged;

    public CultureInfo CurrentCulture => CultureInfo.CurrentUICulture;
    public string Current => CurrentCulture.TwoLetterISOLanguageName;

    public string Get(string key) => key switch
    {
        "Data.AppName" => "Mawaqit Widget",
        "UI.ContextMenu.Show" => "Show",
        "UI.ContextMenu.Hide" => "Hide",
        "UI.ContextMenu.Reposition" => "Re-position",
        "UI.ContextMenu.ManualPosition" => "Manual position",
        "UI.ContextMenu.Display" => "Display",
        "UI.ContextMenu.AlwaysPrimary" => "Always primary",
        "UI.ContextMenu.Quit" => "Quit",
        _ => key
    };

    public void RaiseChanged() => CurrentChanged?.Invoke(this, EventArgs.Empty);
}
