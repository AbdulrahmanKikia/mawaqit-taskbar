using Mawaqit.WinUI.Data;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mawaqit.WinUI.Views;

public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;

    public SettingsWindow()
    {
        InitializeComponent();
        Title = "Mawaqit — Preferences";
        var cfg = AppState.Current.Config;
        mosqueLabel.Text = AppState.Current.MosqueLabel;
        hostKind.SelectedIndex = cfg.Host == HostKind.Strip ? 1 : 0;
        chipSize.SelectedIndex = cfg.ChipSize == ChipSize.Compact ? 0 : 1;
        autostart.IsChecked = cfg.StartWithWindows;
        countdown.SelectedIndex = cfg.Countdown switch
        {
            CountdownTarget.Iqama => 1,
            CountdownTarget.AdhanThenIqama => 2,
            _ => 0
        };
        nEnable.IsChecked = cfg.Notifications.Enabled;
        nAdhan.IsChecked = cfg.Notifications.AtAdhan;
        nIqama.IsChecked = cfg.Notifications.AtIqama;
        nBefore.Value = cfg.Notifications.MinutesBefore;
        nFajr.IsChecked = cfg.Notifications.Fajr;
        nDhuhr.IsChecked = cfg.Notifications.Dhuhr;
        nAsr.IsChecked = cfg.Notifications.Asr;
        nMaghrib.IsChecked = cfg.Notifications.Maghrib;
        nIsha.IsChecked = cfg.Notifications.Isha;
        RefreshFavs();
        Closed += (_, _) =>
        {
            if (ReferenceEquals(_open, this)) _open = null;
        };
    }

    public static void Show()
    {
        if (_open != null)
        {
            _open.Activate();
            return;
        }
        _open = new SettingsWindow();
        _open.Activate();
    }

    private void RefreshFavs()
    {
        favList.ItemsSource = AppState.Current.Config.Favorites
            .Select(f => f.DisplayName + "  (" + f.MasjidId + ")").ToList();
    }

    private void Mosque_Click(object sender, RoutedEventArgs e) => MapWindow.Show();

    private void RemoveFav_Click(object sender, RoutedEventArgs e)
    {
        var i = favList.SelectedIndex;
        if (i < 0 || i >= AppState.Current.Config.Favorites.Count) return;
        AppState.Current.Config.Favorites.RemoveAt(i);
        AppState.Current.Persist();
        RefreshFavs();
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var cfg = AppState.Current.Config;
        var host = hostKind.SelectedIndex == 1 ? HostKind.Strip : HostKind.Injected;
        var size = chipSize.SelectedIndex == 0 ? ChipSize.Compact : ChipSize.Expanded;
        bool hostChanged = cfg.Host != host;
        cfg.ChipSize = size;
        cfg.Host = host;
        cfg.StartWithWindows = autostart.IsChecked == true;
        cfg.Countdown = countdown.SelectedIndex switch
        {
            1 => CountdownTarget.Iqama,
            2 => CountdownTarget.AdhanThenIqama,
            _ => CountdownTarget.Adhan
        };
        cfg.Notifications.Enabled = nEnable.IsChecked == true;
        cfg.Notifications.AtAdhan = nAdhan.IsChecked == true;
        cfg.Notifications.AtIqama = nIqama.IsChecked == true;
        cfg.Notifications.MinutesBefore = (uint)Math.Clamp(nBefore.Value, 0, 60);
        cfg.Notifications.Fajr = nFajr.IsChecked == true;
        cfg.Notifications.Dhuhr = nDhuhr.IsChecked == true;
        cfg.Notifications.Asr = nAsr.IsChecked == true;
        cfg.Notifications.Maghrib = nMaghrib.IsChecked == true;
        cfg.Notifications.Isha = nIsha.IsChecked == true;
        cfg.Notifications.SetupCompleted = true;
        AppState.Current.Persist();
        if (hostChanged)
            TaskBarManager.SwitchHost(host);
        Close();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        updateStatus.Text = "Checking…";
        try
        {
            var latest = await Updater.FetchLatestAsync();
            if (!Updater.IsNewer(latest.Version, AppState.Version))
            {
                updateStatus.Text = $"Up to date ({AppState.Version})";
                return;
            }
            updateStatus.Text = $"Update {latest.Version} available — downloading…";
            var dest = Path.Combine(Path.GetTempPath(), latest.SetupName);
            await Updater.DownloadAsync(latest.SetupUrl, dest);
            updateStatus.Text = "Starting installer…";
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            updateStatus.Text = ex.Message;
        }
    }
}
