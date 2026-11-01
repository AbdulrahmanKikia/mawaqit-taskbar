using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mawaqit.WinUI.Helpers;

internal static class AppDialog
{
    public static XamlRoot? FindXamlRoot()
        => Views.SettingsWindow.XamlRootForDialog
        ?? Views.MapWindow.XamlRootForDialog;

    public static async Task ShowAsync(string title, string message)
    {
        var root = FindXamlRoot();
        if (root is null)
        {
            MessageBox.Warning(message);
            return;
        }

        var dialog = new ContentDialog
        {
            Title = title,
            Content = message,
            CloseButtonText = "OK",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = root,
            RequestedTheme = ThemeHelper.GeneralTheme
        };
        await dialog.ShowAsync();
    }
}
