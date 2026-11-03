using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace Mawaqit.WinUI.Views;

/// <summary>Context menu for the taskbar chip (right-click / long-press / flyout ⋯).</summary>
internal static class ChipMenu
{
    public static void Show(FrameworkElement anchor, Point? position = null)
    {
        var menu = Build();
        if (position is Point p)
            menu.ShowAt(anchor, p);
        else
            menu.ShowAt(anchor);
    }

    // Defaults: Preferences, Manual position (injected), Hide, Quit.
    // Chip size / Countdown / Placement are opt-in via Preferences → chip menu toggles.
    // Change mosque and Qibla stay on the flyout buttons, not this menu.
    public static MenuFlyout Build()
    {
        var cfg = AppState.Current.Config;
        var loc = LocaleManager.Default;
        var menu = new MenuFlyout();
        menu.Items.Add(Item(loc.Get("UI.Preferences"), SettingsWindow.Show));

        if (cfg.MenuShowChipSize)
        {
            var sizeMenu = new MenuFlyoutSubItem { Text = loc.Get("UI.ContextMenu.ChipSize") };
            sizeMenu.Items.Add(CheckedItem(loc.Get("UI.ContextMenu.Compact"), cfg.ChipSize == ChipSize.Compact, () => AppState.Current.ApplyChipSize(ChipSize.Compact)));
            sizeMenu.Items.Add(CheckedItem(loc.Get("UI.ContextMenu.Expanded"), cfg.ChipSize == ChipSize.Expanded, () => AppState.Current.ApplyChipSize(ChipSize.Expanded)));
            menu.Items.Add(sizeMenu);
        }

        if (cfg.MenuShowCountdown)
        {
            var countMenu = new MenuFlyoutSubItem { Text = loc.Get("UI.Countdown") };
            countMenu.Items.Add(CheckedItem(loc.Get("UI.Adhan"), cfg.Countdown == CountdownTarget.Adhan, () => AppState.Current.ApplyCountdown(CountdownTarget.Adhan)));
            countMenu.Items.Add(CheckedItem(loc.Get("UI.Iqama"), cfg.Countdown == CountdownTarget.Iqama, () => AppState.Current.ApplyCountdown(CountdownTarget.Iqama)));
            countMenu.Items.Add(CheckedItem(loc.Get("UI.AdhanThenIqama"), cfg.Countdown == CountdownTarget.AdhanThenIqama, () => AppState.Current.ApplyCountdown(CountdownTarget.AdhanThenIqama)));
            menu.Items.Add(countMenu);
        }

        if (cfg.MenuShowPlacement)
        {
            var hostMenu = new MenuFlyoutSubItem { Text = loc.Get("UI.Placement") };
            hostMenu.Items.Add(CheckedItem(loc.Get("UI.InTaskbar"), cfg.Host == HostKind.Injected, () => AppState.Current.ApplyHostKind(HostKind.Injected)));
            hostMenu.Items.Add(CheckedItem(loc.Get("UI.ScreenStrip"), cfg.Host == HostKind.Strip, () => AppState.Current.ApplyHostKind(HostKind.Strip)));
            menu.Items.Add(hostMenu);
        }

        if (cfg.Host == HostKind.Injected)
        {
            menu.Items.Add(Item(loc.Get("UI.ContextMenu.ManualPosition"), () =>
            {
                TaskBarManager.ManuallyPositionWidget.Execute(null);
            }));
        }

        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(Item(loc.Get("UI.ContextMenu.Hide"), () => TaskBarManager.HideWidget.Execute(null)));
        menu.Items.Add(Item(loc.Get("UI.ContextMenu.Quit"), () => App.Quit.Execute(null)));
        return menu;
    }

    private static MenuFlyoutItem Item(string text, Action action)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => action();
        return item;
    }

    private static ToggleMenuFlyoutItem CheckedItem(string text, bool on, Action action)
    {
        var item = new ToggleMenuFlyoutItem { Text = text, IsChecked = on };
        item.Click += (_, _) => action();
        return item;
    }
}
