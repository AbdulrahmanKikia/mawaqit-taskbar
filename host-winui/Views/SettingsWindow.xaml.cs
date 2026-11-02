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
        AppState.Current.PropertyChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            ThemeHelper.ThemeChanged -= OnThemeChanged;
            LocaleManager.Default.CurrentChanged -= OnLocaleChanged;
            AppState.Current.PropertyChanged -= OnStateChanged;
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
        mosqueCard.Description = AppState.Current.MosqueName;
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
        LoadTimesSettings();
    }

    private static readonly CalculationMethod[] MethodOrder = Enum.GetValues<CalculationMethod>();

    private void LoadTimesSettings()
    {
        var cfg = AppState.Current.Config;
        timesSource.SelectedIndex = (int)cfg.Times;
        locationAuto.IsOn = cfg.Location.Auto;
        methodBox.Items.Clear();
        methodBox.Items.Add(new ComboBoxItem());
        foreach (var m in MethodOrder)
            methodBox.Items.Add(new ComboBoxItem { Content = MethodNames.Long(m) });
        methodBox.SelectedIndex = cfg.Calculation.Method is { } cm ? Array.IndexOf(MethodOrder, cm) + 1 : 0;
        asrBox.SelectedIndex = cfg.Calculation.Asr switch { AsrSchool.Standard => 1, AsrSchool.Hanafi => 2, _ => 0 };
        highLatBox.SelectedIndex = (int)cfg.Calculation.HighLatitude;
        var iq = cfg.Mosque?.Iqama;
        iqFajr.Text = iq?.Fajr ?? "";
        iqDhuhr.Text = iq?.Dhuhr ?? "";
        iqAsr.Text = iq?.Asr ?? "";
        iqMaghrib.Text = iq?.Maghrib ?? "";
        iqIsha.Text = iq?.Isha ?? "";
        RefreshTimesState();
    }

    /// <summary>Descriptions and which cards apply to the current choice.</summary>
    private void RefreshTimesState()
    {
        var st = AppState.Current;
        var cfg = st.Config;
        var loc = LocaleManager.Default;
        var usesLocation = cfg.Times != TimesSource.Mosque;
        locationCard.Visibility = usesLocation ? Visibility.Visible : Visibility.Collapsed;
        locationCard.Description = st.LocationStatus;
        locationOffCard.Visibility = usesLocation && cfg.Location.Auto && LocationService.Access == LocationAccess.Denied
            ? Visibility.Visible : Visibility.Collapsed;
        pickLocationBtn.Visibility = cfg.Location.Auto ? Visibility.Collapsed : Visibility.Visible;
        var (method, asr) = st.EffectiveCalculation;
        timesExp.Description = st.SourceDetail.Length > 0 ? st.SourceDetail : loc.Get("UI.TimesFromTimetable");
        if (methodBox.Items.Count > 0 && methodBox.Items[0] is ComboBoxItem auto)
            auto.Content = string.Format(loc.Get("UI.MethodAuto"), MethodNames.Short(method));
        if (asrBox.Items[0] is ComboBoxItem asrAuto)
            asrAuto.Content = string.Format(loc.Get("UI.MethodAuto"),
                loc.Get(asr == AsrSchool.Hanafi ? "UI.AsrHanafi" : "UI.AsrStandard"));
        var mosque = cfg.Mosque;
        iqamaCard.Visibility = mosque is { Provider: MosqueProvider.OpenStreetMap } ? Visibility.Visible : Visibility.Collapsed;
        websiteLink.Visibility = mosque?.Website is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Times_Changed(object sender, SelectionChangedEventArgs e) => ApplyTimes();

    private void TimesToggle_Changed(object sender, RoutedEventArgs e) => ApplyTimes();

    private void ApplyTimes()
    {
        if (_loading) return;
        var cfg = AppState.Current.Config;
        cfg.Times = (TimesSource)Math.Max(0, timesSource.SelectedIndex);
        cfg.Location.Auto = locationAuto.IsOn;
        cfg.Calculation.Method = methodBox.SelectedIndex > 0 ? MethodOrder[methodBox.SelectedIndex - 1] : null;
        cfg.Calculation.Asr = asrBox.SelectedIndex switch { 1 => AsrSchool.Standard, 2 => AsrSchool.Hanafi, _ => null };
        cfg.Calculation.HighLatitude = (HighLatitudeRule)Math.Max(0, highLatBox.SelectedIndex);
        AppState.Current.ApplyTimesSettings();
        RefreshTimesState();
    }

    private void PickLocation_Click(object sender, RoutedEventArgs e) => MapWindow.ShowForLocation();

    private void LocationSettings_Click(object sender, RoutedEventArgs e)
        => _ = LocationService.OpenWindowsLocationSettingsAsync();

    private void SaveIqama_Click(object sender, RoutedEventArgs e)
    {
        var iq = Iqama.Parse([iqFajr.Text, iqDhuhr.Text, iqAsr.Text, iqMaghrib.Text, iqIsha.Text]);
        AppState.Current.ApplyMosqueIqama(iq);
        _loading = true;
        LoadTimesSettings();
        _loading = false;
    }

    private void Website_Click(object sender, RoutedEventArgs e)
    {
        if (AppState.Current.Config.Mosque?.Website is { } site && Uri.TryCreate(site, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https")
            _ = Windows.System.Launcher.LaunchUriAsync(uri);
    }

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(AppState.LocationStatus) or nameof(AppState.SourceDetail) or nameof(AppState.MosqueName)))
            return;
        void Go()
        {
            mosqueCard.Description = AppState.Current.MosqueName;
            RefreshTimesState();
        }
        if (DispatcherQueue.HasThreadAccess) Go();
        else DispatcherQueue.TryEnqueue(Go);
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
        mosqueCard.Description = AppState.Current.MosqueName;
        changeMosqueBtn.Content = loc.Get("UI.ChangeMosque");
        timesExp.Header = loc.Get("UI.PrayerTimes");
        timesSourceCard.Header = loc.Get("UI.TimesFrom");
        SetCombo(timesSource, 0, loc.Get("UI.TimesFromMosque"));
        SetCombo(timesSource, 1, loc.Get("UI.TimesFromLocation"));
        SetCombo(timesSource, 2, loc.Get("UI.TimesFromAutomatic"));
        locationCard.Header = loc.Get("UI.MyLocation");
        locationAuto.OnContent = loc.Get("UI.DetectLocation");
        locationAuto.OffContent = loc.Get("UI.DetectLocation");
        pickLocationBtn.Content = loc.Get("UI.PickOnMap");
        locationOffCard.Header = loc.Get("UI.LocationDenied");
        locationSettingsLink.Content = loc.Get("UI.OpenLocationSettings");
        methodCard.Header = loc.Get("UI.Method");
        methodCard.Description = loc.Get("UI.MethodDesc");
        asrCard.Header = loc.Get("UI.Asr");
        SetCombo(asrBox, 1, loc.Get("UI.AsrStandard"));
        SetCombo(asrBox, 2, loc.Get("UI.AsrHanafi"));
        highLatCard.Header = loc.Get("UI.HighLatitude");
        highLatCard.Description = loc.Get("UI.HighLatitudeDesc");
        SetCombo(highLatBox, 0, loc.Get("UI.HighLatAngle"));
        SetCombo(highLatBox, 1, loc.Get("UI.HighLatMiddle"));
        SetCombo(highLatBox, 2, loc.Get("UI.HighLatSeventh"));
        iqamaCard.Header = loc.Get("UI.MosqueIqama");
        iqamaCard.Description = loc.Get("UI.MosqueIqamaDesc");
        saveIqamaBtn.Content = loc.Get("UI.Save");
        websiteLink.Content = loc.Get("UI.MosqueWebsite");
        foreach (var (box, kind) in new[] { (iqFajr, "Fajr"), (iqDhuhr, "Dhuhr"), (iqAsr, "Asr"), (iqMaghrib, "Maghrib"), (iqIsha, "Isha") })
            box.Header = loc.Prayer(kind);
        RefreshTimesState();
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

    private static void SetCombo(ComboBox box, int i, string text)
    {
        if (i < box.Items.Count && box.Items[i] is ComboBoxItem item)
            item.Content = text;
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
            mosqueCard.Description = AppState.Current.MosqueName;
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
            await Updater.DownloadVerifyAndLaunchAsync(latest);
            updateStatus.Text = loc.Get("UI.StartingInstaller");
        }
        catch (UpdateVerificationException ex)
        {
            Serilog.Log.Error(ex, "Update verification failed");
            updateStatus.Text = loc.Get("UI.UpdateNotVerified");
            await AppDialog.ShowAsync(loc.Get("UI.Update"), loc.Get("UI.UpdateNotVerified"));
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Update check failed");
            updateStatus.Text = loc.Get("UI.UpdateFailed");
            await AppDialog.ShowAsync(loc.Get("UI.Update"), loc.Get("UI.UpdateFailed"));
        }
    }
}
