// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace Mawaqit.WinUI.Helpers;

internal static class ThemeHelper
{
    private static DispatcherQueue? _ui;

    public static event Action? ThemeChanged;

    public static ElementTheme GeneralTheme
        => SystemInfos.IsAppsLightThemeUsed() == true ? ElementTheme.Light : ElementTheme.Dark;

    public static ElementTheme TaskbarTheme
        => SystemInfos.IsLightThemeUsed() == true ? ElementTheme.Light : ElementTheme.Dark;

    public static void Initialize(DispatcherQueue dispatcher)
    {
        _ui = dispatcher;
        ThemeWatcher.Ensure();
    }

    public static void Notify()
    {
        void Fire() => ThemeChanged?.Invoke();
        if (_ui is null || _ui.HasThreadAccess) Fire();
        else _ui.TryEnqueue(Fire);
    }

    public static void ApplyTo(FrameworkElement? element, ElementTheme theme)
    {
        if (element is null) return;
        element.RequestedTheme = theme;
    }
}
