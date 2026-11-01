using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mawaqit.WinUI.Views;

public sealed partial class SettingsWindow : Window
{
    private static SettingsWindow? _open;
    private bool _loading = true;

    internal static XamlRoot? XamlRootForDialog => _open?.Content?.XamlRoot;

    public SettingsWindow()
    {
        InitializeComponent();
        WindowChrome.Apply(this, AppTitleBar, 720, 640);
        LoadFromConfig();
        ApplyLocale();
        ThemeHelper.ThemeChanged += OnThemeChanged;
        LocaleManager.Default.CurrentChanged += OnLocaleChanged;
        Closed += (_, _) =>
        {
            ThemeHelper.ThemeChanged -= OnThemeChanged;
            LocaleManager.Default.CurrentChanged -= OnLocaleChanged;
            if (ReferenceEquals(_open, this)) _open = null;
        };
        _loading = false;
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

    private void LoadFromConfig()
    {
        var cfg = AppState.Current.Config;
        mosqueCard.Description = AppState.Current.MosqueLabel;
        aboutCard.Description = AppState.Version;
        langBox.SelectedIndex = LocaleManager.ComboIndex(cfg.Language);
        hostKind.SelectedIndex = cfg.Host == HostKind.Strip ? 1 : 0;
        chipSize.SelectedIndex = cfg.ChipSize == ChipSize.Compact ? 0 : 1;
        autostart.IsOn = cfg.StartWithWindows;
        countdown.SelectedIndex = cfg.Countdown switch
        {
            CountdownTarget.Iqama => 1,
            CountdownTarget.AdhanThenIqama => 2,
            _ => 0
        };
        nEnable.IsOn = cfg.Notifications.Enabled;
        nAdhan.IsOn = cfg.Notifications.AtAdhan;
        nIqama.IsOn = cfg.Notifications.AtIqama;
        nBefore.Value = cfg.Notifications.MinutesBefore;
        nFajr.IsOn = cfg.Notifications.Fajr;
        nDhuhr.IsOn = cfg.Notifications.Dhuhr;
        nAsr.IsOn = cfg.Notifications.Asr;
        nMaghrib.IsOn = cfg.Notifications.Maghrib;
        nIsha.IsOn = cfg.Notifications.Isha;
        if (!cfg.Notifications.SetupCompleted)
        {
            cfg.Notifications.SetupCompleted = true;
            AppState.Current.Persist();
        }
        RefreshFavs();
    }

    private void ApplyLocale()
    {
        var loc = LocaleManager.Default;
        loc.ApplyFlow(root);
        Title = loc.Get("Data.AppName");
        titleBarTb.Text = loc.Get("Data.AppName");
        pageTitle.Text = loc.Get("UI.Preferences");
        langCard.Header = loc.Get("UI.Language");
        langCard.Description = loc.Get("UI.LanguageDesc");
        SetItem(0, loc.Get("UI.LangSystem"));
        SetItem(1, loc.Get("UI.LangEn"));
        SetItem(2, loc.Get("UI.LangFr"));
        SetItem(3, loc.Get("UI.LangAr"));
        mosqueCard.Header = loc.Get("UI.Mosque");
        mosqueCard.Description = AppState.Current.MosqueLabel;
        changeMosqueBtn.Content = loc.Get("UI.ChangeMosque");
        countdownExp.Header = loc.Get("UI.Countdown");
        countdownExp.Description = loc.Get("UI.CountdownDesc");
        countdownToCard.Header = loc.Get("UI.CountDownTo");
        cdAdhan.Content = loc.Get("UI.Adhan");
        cdIqama.Content = loc.Get("UI.Iqama");
        cdBoth.Content = loc.Get("UI.AdhanThenIqama");
        countdownHint.Description = loc.Get("UI.AdhanThenIqamaHint");
        placeExp.Header = loc.Get("UI.Placement");
        placeExp.Description = loc.Get("UI.PlacementDesc");
        placeCard.Header = loc.Get("UI.Location");
        hostInject.Content = loc.Get("UI.InTaskbar");
        hostStrip.Content = loc.Get("UI.ScreenStrip");
        placeHint.Description = loc.Get("UI.StripHint");
        chipExp.Header = loc.Get("UI.Chip");
        chipExp.Description = loc.Get("UI.ChipDesc");
        sizeCard.Header = loc.Get("UI.Size");
        sizeCompact.Content = loc.Get("UI.Compact");
        sizeExpanded.Content = loc.Get("UI.Expanded");
        autostartCard.Header = loc.Get("UI.Autostart");
        notifExp.Header = loc.Get("UI.Notifications");
        notifExp.Description = loc.Get("UI.NotificationsDesc");
        nAdhanCard.Header = loc.Get("UI.AtAdhan");
        nIqamaCard.Header = loc.Get("UI.AtIqama");
        nBeforeCard.Header = loc.Get("UI.MinutesBefore");
        nFajrCard.Header = loc.Prayer("Fajr");
        nDhuhrCard.Header = loc.Prayer("Dhuhr");
        nAsrCard.Header = loc.Prayer("Asr");
        nMaghribCard.Header = loc.Prayer("Maghrib");
        nIshaCard.Header = loc.Prayer("Isha");
        favExp.Header = loc.Get("UI.Favorites");
        favExp.Description = loc.Get("UI.FavoritesDesc");
        removeFavBtn.Content = loc.Get("UI.RemoveFavorite");
        aboutExp.Header = loc.Get("UI.About");
        aboutExp.Description = loc.Get("Data.AppName");
        aboutCard.Header = loc.Get("UI.Version");
        aboutCard.Description = AppState.Version;
        updateBtn.Content = loc.Get("UI.CheckUpdates");
    }

    private void SetItem(int i, string text)
    {
        if (i < langBox.Items.Count && langBox.Items[i] is ComboBoxItem item)
            item.Content = text;
    }

    private void RefreshFavs()
    {
        favList.ItemsSource = AppState.Current.Config.Favorites
            .Select(f => f.DisplayName + "  (" + f.MasjidId + ")").ToList();
    }

    private void OnThemeChanged()
    {
        void Go()
        {
            WindowChrome.ApplyTheme(this);
            mosqueCard.Description = AppState.Current.MosqueLabel;
        }
        if (DispatcherQueue.HasThreadAccess) Go();
        else DispatcherQueue.TryEnqueue(Go);
    }

    private void OnLocaleChanged(object? sender, EventArgs e)
    {
        if (DispatcherQueue.HasThreadAccess) ApplyLocale();
        else DispatcherQueue.TryEnqueue(ApplyLocale);
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

    private void Lang_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        AppState.Current.ApplyLanguage(LocaleManager.FromCombo(langBox.SelectedIndex));
    }

    private void Any_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        Apply();
    }

    private void Number_Changed(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        Apply();
    }

    private void Apply()
    {
        var cfg = AppState.Current.Config;
        var host = hostKind.SelectedIndex == 1 ? HostKind.Strip : HostKind.Injected;
        var size = chipSize.SelectedIndex == 0 ? ChipSize.Compact : ChipSize.Expanded;
        var count = countdown.SelectedIndex switch
        {
            1 => CountdownTarget.Iqama,
            2 => CountdownTarget.AdhanThenIqama,
            _ => CountdownTarget.Adhan
        };

        cfg.StartWithWindows = autostart.IsOn;
        cfg.Notifications.Enabled = nEnable.IsOn;
        cfg.Notifications.AtAdhan = nAdhan.IsOn;
        cfg.Notifications.AtIqama = nIqama.IsOn;
        cfg.Notifications.MinutesBefore = (uint)Math.Clamp(nBefore.Value, 0, 60);
        cfg.Notifications.Fajr = nFajr.IsOn;
        cfg.Notifications.Dhuhr = nDhuhr.IsOn;
        cfg.Notifications.Asr = nAsr.IsOn;
        cfg.Notifications.Maghrib = nMaghrib.IsOn;
        cfg.Notifications.Isha = nIsha.IsOn;
        cfg.Notifications.SetupCompleted = true;

        if (cfg.ChipSize != size) AppState.Current.ApplyChipSize(size);
        if (cfg.Countdown != count) AppState.Current.ApplyCountdown(count);
        if (cfg.Host != host) AppState.Current.ApplyHostKind(host);
        else AppState.Current.Persist();
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        var loc = LocaleManager.Default;
        updateStatus.Text = loc.Get("UI.Checking");
        try
        {
            var latest = await Updater.FetchLatestAsync();
            if (!Updater.IsNewer(latest.Version, AppState.Version))
            {
                updateStatus.Text = string.Format(loc.Get("UI.UpToDate"), AppState.Version);
                return;
            }
            updateStatus.Text = string.Format(loc.Get("UI.UpdateAvailable"), latest.Version);
            var dest = Path.Combine(Path.GetTempPath(), latest.SetupName);
            await Updater.DownloadAsync(latest.SetupUrl, dest);
            updateStatus.Text = loc.Get("UI.StartingInstaller");
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            updateStatus.Text = ex.Message;
            await AppDialog.ShowAsync(loc.Get("UI.Update"), ex.Message);
        }
    }
}
