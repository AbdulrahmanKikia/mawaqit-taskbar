// Adapted from Awqat Salaat (https://github.com/Khiro95/Awqat-Salaat)
// Copyright (c) 2023 Khiro
// Licensed under the MIT License.
using Microsoft.UI.Xaml;

namespace Mawaqit.WinUI.Helpers;

internal static class ThemeHelper
{
    public static event Action? ThemeChanged;

    public static ElementTheme GeneralTheme
        => SystemInfos.IsAppsLightThemeUsed() == true ? ElementTheme.Light : ElementTheme.Dark;

    public static void Notify() => ThemeChanged?.Invoke();
}
