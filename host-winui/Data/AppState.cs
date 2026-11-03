using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Mawaqit.WinUI.Helpers;
using Mawaqit.WinUI.Views;
using Serilog;
using Windows.Networking.Connectivity;

namespace Mawaqit.WinUI.Data;

public sealed class PrayerRow
{
    public string Name { get; init; } = "";
    public string Adhan { get; init; } = "";
    public string Iqama { get; init; } = "";
    public bool IsNext { get; init; }
    public string TrackMark => IsNext ? "●" : "";
}

public sealed class AppState : INotifyPropertyChanged
{
    #region Constants and shared state

    public const string Version = "0.5.2";
    public const string Repo = "AbdulrahmanKikia/mawaqit-taskbar";

    /// <summary>Times are re-read this often so mosque edits (iqama changes) show up.</summary>
    private static readonly TimeSpan RefreshEvery = TimeSpan.FromHours(6);
    /// <summary>Saved times older than this are labelled as such in the flyout.</summary>
    private static readonly TimeSpan StaleAfter = TimeSpan.FromDays(2);

    public static AppState Current { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<DisplayMode>? DisplayModeChanged;

    public AppConfig Config { get; private set; } = new();
    public ObservableCollection<PrayerRow> Slots { get; } = new();

    public ICommand RefreshCommand { get; }
    public ICommand OpenSettingsCommand { get; }
    public ICommand ChangeMosqueCommand { get; }
    public ICommand SetChipSizeCommand { get; }
    public ICommand SetHostKindCommand { get; }

    private readonly MawaqitClient _client = new();
    private readonly TimetableStore _store = new(AppConfigStore.CacheDir);
    private readonly RetryBackoff _backoff = new();
    private readonly AlertLedger _ledger = new();
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _tick;
    private MosqueTimetable? _table;
    private ITimesSource? _source;
    private ActiveSource _active = new(ActiveKind.None, null);
    private DispatcherQueueTimer? _locationTimer;
    private bool _locating;
    private DateOnly _builtFor;
    private List<PrayerSlot> _window = new();      // yesterday, today, tomorrow
    private List<PrayerSlot> _today = new();
    private DateTimeOffset _lastSuccess = DateTimeOffset.MinValue;
    private bool _fetching;
    private string _slotsFingerprint = "";
    private DisplayMode _lastDisplayMode = DisplayMode.Expanded;

    #endregion

    #region Bound UI properties

    public string ChipName { get; private set; } = "Mawaqit";
    public string ChipTime { get; private set; } = "";
    public string ChipCountdown { get; private set; } = "…";
    public string ChipLine => Config.ChipSize == ChipSize.Compact
        ? ChipCountdown
        : $"{ChipName}  {ChipTime}  ·  {ChipCountdown}";
    /// <summary>Where today's times are for: the mosque, or the user's place when calculated for them.</summary>
    public string MosqueLabel => _active.Kind == ActiveKind.LocationCalculated
        ? (string.IsNullOrWhiteSpace(Config.Location.Name) ? LocaleManager.Default.Get("UI.YourLocation") : Config.Location.Name)
        : Config.Mosque?.DisplayName ?? LocaleManager.Default.Get("UI.NoMosque");
    /// <summary>The chosen mosque, whatever the times source.</summary>
    public string MosqueName => Config.Mosque?.DisplayName ?? LocaleManager.Default.Get("UI.NoMosque");
    /// <summary>How the times were obtained ("Calculated · ISNA", "350 km from …"), or "" for a mosque timetable.</summary>
    public string SourceDetail { get; private set; } = "";
    public string TrackingLabel { get; private set; } = "";
    public string NextHeadline { get; private set; } = "";
    public string Status { get; private set; } = "loading";
    /// <summary>"" when times are fresh; otherwise says the widget is showing saved times.</summary>
    public string FreshnessLabel { get; private set; } = "";
    public bool HasError { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsConfigured => Config.Mosque != null || Config.Location.Point != null;
    public string LocationStatus => LocationService.Access switch
    {
        LocationAccess.Denied => LocaleManager.Default.Get("UI.LocationDenied"),
        LocationAccess.Unavailable => LocaleManager.Default.Get("UI.LocationUnavailable"),
        _ => Config.Location.Point is null ? LocaleManager.Default.Get("UI.LocationUnknown") : MosqueLabelForLocation,
    };
    private string MosqueLabelForLocation => string.IsNullOrWhiteSpace(Config.Location.Name)
        ? FormattableString.Invariant($"{Config.Location.Latitude:0.###}, {Config.Location.Longitude:0.###}")
        : Config.Location.Name;
    private static string RegionCode => System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
    public (CalculationMethod Method, AsrSchool Asr) EffectiveCalculation => Methods.Resolve(Config.Calculation, RegionCode);
    public bool IsNear { get; private set; }
    /// <summary>How far we are from the previous prayer to the next one, 0..1 (flyout progress bar).</summary>
    public double NextProgress { get; private set; }

    #endregion

    #region Construction

    private AppState()
    {
        RefreshCommand = new RelayCommand(_ => _ = RefreshAsync(force: true));
        OpenSettingsCommand = new RelayCommand(_ => Views.SettingsWindow.Show());
        ChangeMosqueCommand = new RelayCommand(_ => Views.MapWindow.Show());
        SetChipSizeCommand = new RelayCommand(o =>
        {
            if (o is ChipSize size)
                ApplyChipSize(size);
            else if (o is string s && Enum.TryParse<ChipSize>(s, true, out var parsed))
                ApplyChipSize(parsed);
        });
        SetHostKindCommand = new RelayCommand(o =>
        {
            HostKind kind = o switch
            {
                HostKind h => h,
                string s when s.Equals("strip", StringComparison.OrdinalIgnoreCase)
                    || s.Equals("overlay", StringComparison.OrdinalIgnoreCase) => HostKind.Strip,
                _ => HostKind.Injected
            };
            ApplyHostKind(kind);
        });
    }

    #endregion

    #region Config load and preferences

    public void Load()
    {
        Config = AppConfigStore.Load();
        LocaleManager.Default.Apply(Config.Language);
        _lastDisplayMode = ChipLayout.ToDisplayMode(Config.ChipSize);
        UseTimetable(Config.Mosque is { Provider: MosqueProvider.Mawaqit } m ? _store.Load(m.MasjidId) : null);
    }

    public void Persist()
    {
        AppConfigStore.Save(Config);
        Autostart.Apply(Config.StartWithWindows);
        Raise(nameof(MosqueLabel), nameof(IsConfigured), nameof(ChipLine), nameof(TrackingLabel), nameof(NextHeadline));
        RaiseDisplayMode();
    }

    public void ApplyChipSize(ChipSize size)
    {
        if (Config.ChipSize == size) return;
        Config.ChipSize = size;
        Persist();
        Tick();
    }

    public void ApplyCountdown(CountdownTarget target)
    {
        if (Config.Countdown == target) return;
        Config.Countdown = target;
        Persist();
        Tick();
    }

    public void ApplyHostKind(HostKind kind)
    {
        if (Config.Host == kind) return;
        Config.Host = kind;
        Persist();
        TaskBarManager.SwitchHost(kind);
    }

    public void ApplyLanguage(string preference)
    {
        var next = LocaleManager.Normalize(preference);
        Config.Language = next;
        Persist();
        LocaleManager.Default.Apply(next);
        ResolveSource();
        Tick();
    }

    /// <summary>Times source or calculation settings changed in Preferences.</summary>
    public void ApplyTimesSettings()
    {
        Persist();
        ResolveSource();
        Tick();
        if (WantsLocation) _ = UpdateLocationAsync();
    }

    /// <summary>The user picked a spot on the map as "my location".</summary>
    public async Task SetManualLocationAsync(MapLocation pick)
    {
        Config.Location.Auto = false;
        Config.Location.Latitude = pick.Lat;
        Config.Location.Longitude = pick.Lon;
        Config.Location.Name = pick.Label;
        if (Config.Times == TimesSource.Mosque) Config.Times = TimesSource.Location;
        Persist();
        ResolveSource();
        Tick();
        if (pick.Label.Length == 0) await NameLocationAsync(new GeoPoint(pick.Lat, pick.Lon));
    }

    #endregion

    #region Lifecycle

    public void Start()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _tick = _dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        _locationTimer = _dispatcher.CreateTimer();
        _locationTimer.Interval = TimeSpan.FromMinutes(30);
        _locationTimer.Tick += (_, _) => _ = UpdateLocationAsync();
        _locationTimer.Start();
        NetworkInformation.NetworkStatusChanged += _ => _dispatcher.TryEnqueue(() =>
        {
            Log.Information("Network status changed; retrying soon if the last refresh failed");
            _backoff.RetrySoon(DateTimeOffset.Now);
            _ = UpdateLocationAsync();
        });
        Tick();
        _ = RefreshAsync(force: true);
        _ = UpdateLocationAsync();
        if (!IsConfigured)
            Views.MapWindow.Show();
        else if (!Config.Notifications.SetupCompleted)
            Views.SettingsWindow.Show();
    }

    #endregion

    #region Mosque and favorites

    public async Task ApplyMosqueAsync(MapSelect pick)
    {
        var sel = new MosqueSelection
        {
            MasjidId = pick.Slug,
            DisplayName = pick.Label,
            Latitude = pick.Lat,
            Longitude = pick.Lon,
            Provider = pick.Provider,
            Website = pick.Website,
            // Keep iqama times the user already typed for this mosque.
            Iqama = Config.Favorites.Append(Config.Mosque).FirstOrDefault(m => m?.MasjidId == pick.Slug)?.Iqama,
        };
        Config.Mosque = sel;
        if (pick.Favorite && Config.AddFavorite(sel) == FavoriteResult.Full)
            _ = AppDialog.ShowAsync(LocaleManager.Default.Get("UI.Favorites"), LocaleManager.Default.Get("UI.FavoritesFull"));
        Persist();
        await SwitchToCurrentMosqueAsync();
    }

    public async Task SwitchFavoriteAsync(MosqueSelection sel)
    {
        Config.Mosque = sel.Copy();
        Persist();
        await SwitchToCurrentMosqueAsync();
    }

    public Task CycleFavoriteAsync(int delta)
    {
        var favs = Config.Favorites;
        if (favs.Count < 2) return Task.CompletedTask;
        var currentId = Config.Mosque?.MasjidId;
        var idx = favs.FindIndex(f => f.MasjidId.Equals(currentId, StringComparison.OrdinalIgnoreCase));
        // Current mosque is not favorited: next → first, prev → last (don't pretend we're already on [0]).
        MosqueSelection next;
        if (idx < 0)
            next = delta >= 0 ? favs[0] : favs[^1];
        else
            next = favs[(idx + delta % favs.Count + favs.Count) % favs.Count];
        return SwitchFavoriteAsync(next);
    }

    private async Task SwitchToCurrentMosqueAsync()
    {
        _backoff.Succeeded();
        UseTimetable(Config.Mosque is { Provider: MosqueProvider.Mawaqit } m ? _store.Load(m.MasjidId) : null);
        Tick();
        await RefreshAsync(force: true);
    }

    public MawaqitClient MapClient => _client;

    /// <summary>Where the Qibla is computed from: where the user is when known, else the mosque.</summary>
    public MosqueSelection? QiblaOrigin => Config.Location.Point is { } here
        ? new MosqueSelection { DisplayName = MosqueLabelForLocation, Latitude = here.Latitude, Longitude = here.Longitude }
        : Config.Mosque is { HasLocation: true } m ? m : null;

    /// <summary>The mosque's own iqama times, for mosques without a Mawaqit timetable.</summary>
    public void ApplyMosqueIqama(DayIqama? iqama)
    {
        if (Config.Mosque is not { } m) return;
        m.Iqama = iqama;
        foreach (var fav in Config.Favorites.Where(f => f.MasjidId == m.MasjidId)) fav.Iqama = iqama;
        Persist();
        ResolveSource();
        Tick();
    }

    #endregion

    #region Location

    private bool WantsLocation => Config.Times != TimesSource.Mosque && Config.Location.Auto;

    /// <summary>Follow Windows location services when the user asked for times where they are.</summary>
    public async Task UpdateLocationAsync()
    {
        if (_locating || !WantsLocation) return;
        _locating = true;
        try
        {
            var here = await LocationService.TryGetAsync();
            Raise(nameof(LocationStatus));
            if (here is not { } p) return;
            var old = Config.Location.Point;
            // City-level moves only; Wi-Fi fixes wander by a few hundred metres.
            if (old is { } o && Qibla.GreatCircleKm(o.Latitude, o.Longitude, p.Latitude, p.Longitude) < 2
                && !string.IsNullOrWhiteSpace(Config.Location.Name)) return;
            Log.Information("Location updated to {Lat:0.###}, {Lon:0.###}", p.Latitude, p.Longitude);
            Config.Location.Latitude = p.Latitude;
            Config.Location.Longitude = p.Longitude;
            Config.Location.Name = "";
            Persist();
            ResolveSource();
            Tick();
            await NameLocationAsync(p);
        }
        finally
        {
            _locating = false;
        }
    }

    private async Task NameLocationAsync(GeoPoint p)
    {
        try
        {
            if (await _client.PlaceNameAsync(p) is { } name && Config.Location.Point == p)
            {
                Config.Location.Name = name;
                Persist();
                ResolveSource();
            }
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException)
        {
            // Offline: keep showing coordinates until the next update can name the place.
            Log.Information(ex, "Could not name the location yet");
        }
    }

    #endregion

    #region Refresh and offline cache

    public async Task RefreshAsync(bool force)
    {
        if (_fetching) return;
        var mosque = Config.Mosque;
        // Only Mawaqit mosques have a timetable to download; everything else is calculated offline.
        if (mosque is not { Provider: MosqueProvider.Mawaqit })
        {
            ResolveSource();
            Tick();
            return;
        }
        if (!force && !_backoff.IsDue(DateTimeOffset.Now)
            && DateTimeOffset.Now - _lastSuccess < RefreshEvery) return;

        _fetching = true;
        IsRefreshing = true;
        Raise(nameof(IsRefreshing));
        try
        {
            var (fresh, report) = await _client.FetchTimetableAsync(mosque.MasjidId);
            if (Config.Mosque?.MasjidId != mosque.MasjidId) return; // user switched mosque meanwhile

            var table = report switch
            {
                { CalendarTrusted: true } => fresh,
                // Only the published day was usable: add it to the saved year.
                { LiveTimesMatched: not null } => MergeIntoCache(fresh),
                // Part of the calendar failed validation: a saved year beats it; otherwise use the valid days.
                _ => _table is { } old && old.Slug == fresh.Slug && old.Days.Count > fresh.Days.Count ? old : fresh,
            };
            if (MosqueSlug.IsValid(table.Slug)) _store.Save(table);
            BackfillLocation(fresh);
            UseTimetable(table);

            _backoff.Succeeded();
            _lastSuccess = DateTimeOffset.Now;
            HasError = false;
            Status = mosque.DisplayName;
        }
        catch (Exception ex)
        {
            _backoff.Failed(DateTimeOffset.Now);
            Log.Warning(ex, "Refresh failed (attempt {Failures}); next try at {Next}", _backoff.Failures, _backoff.NextAttempt);
            Status = ex.Message;
            ResolveSource();
            HasError = _today.Count == 0;
        }
        finally
        {
            _fetching = false;
            IsRefreshing = false;
            UpdateFreshness();
            Raise(nameof(IsRefreshing), nameof(HasError), nameof(Status));
            Tick();
        }
    }

    /// <summary>The page's calendar was not trusted: keep the saved year and only take the
    /// day Mawaqit published, so one odd fetch cannot wipe the offline cache.</summary>
    private MosqueTimetable MergeIntoCache(MosqueTimetable fresh)
    {
        if (_table is not { } old || old.Slug != fresh.Slug) return fresh;
        old.OverlayPublished(fresh);
        return old;
    }

    private void BackfillLocation(MosqueTimetable table)
    {
        if (table.Latitude is not { } lat || table.Longitude is not { } lon) return;
        var changed = false;
        foreach (var sel in Config.Favorites.Append(Config.Mosque).OfType<MosqueSelection>())
        {
            if (sel.MasjidId != table.Slug || sel.HasLocation) continue;
            sel.Latitude = lat;
            sel.Longitude = lon;
            changed = true;
        }
        if (changed) Persist();
    }

    #endregion

    #region Source resolution and freshness

    private void UseTimetable(MosqueTimetable? table)
    {
        _table = table;
        ResolveSource();
    }

    /// <summary>Decide where today's times come from and rebuild the schedule from it.</summary>
    private void ResolveSource()
    {
        var hasTable = _table is { } t && t.Slug == Config.Mosque?.MasjidId;
        _active = SourcePicker.Pick(Config, hasTable);
        var (method, asr) = EffectiveCalculation;
        var highLat = Config.Calculation.HighLatitude;
        // Days the timetable lacks (unpublished or rejected) are calculated for the mosque instead of left blank.
        ITimesSource? mosqueCalc = Config.Mosque?.Point is { } mp
            ? new CalculatedTimes(mp, method, asr, highLat, TimeZoneInfo.Local)
            : null;
        _source = _active switch
        {
            { Kind: ActiveKind.MosqueTimetable } when _table is { } table => new FallbackTimes(table, mosqueCalc),
            { Kind: ActiveKind.MosqueCalculated, Point: { } p } =>
                new CalculatedTimes(p, method, asr, highLat, TimeZoneInfo.Local, Config.Mosque?.Iqama),
            { Kind: ActiveKind.LocationCalculated, Point: { } p } =>
                new CalculatedTimes(p, method, asr, highLat, TimeZoneInfo.Local),
            _ => null,
        };

        var loc = LocaleManager.Default;
        var calculated = string.Format(loc.Get("UI.CalculatedWith"), MethodNames.Short(method));
        SourceDetail = _active switch
        {
            { Kind: ActiveKind.LocationCalculated, AwayFromMosqueKm: { } km } =>
                string.Format(loc.Get("UI.AwayFromMosque"), km.ToString("N0", loc.CurrentCulture), MosqueName) + " · " + calculated,
            { Kind: ActiveKind.MosqueCalculated or ActiveKind.LocationCalculated } => calculated,
            _ when _source is FallbackTimes f && f.UsesFallback(DateOnly.FromDateTime(DateTime.Now)) =>
                string.Format(loc.Get("UI.TimetableGap"), calculated),
            _ => "",
        };

        _builtFor = default;
        _slotsFingerprint = "";
        UpdateFreshness();
        Raise(nameof(MosqueLabel), nameof(MosqueName), nameof(SourceDetail), nameof(IsConfigured), nameof(LocationStatus));
    }

    private void UpdateFreshness()
    {
        string label = "";
        if (_active.Kind == ActiveKind.MosqueTimetable && _table is { } t
            && (_backoff.Failures > 0 || DateTimeOffset.Now - t.FetchedAt > StaleAfter))
        {
            var when = t.FetchedAt.ToLocalTime().ToString("d MMM", LocaleManager.Default.CurrentCulture);
            label = string.Format(LocaleManager.Default.Get("UI.SavedTimes"), when);
        }
        if (label == FreshnessLabel) return;
        FreshnessLabel = label;
        Raise(nameof(FreshnessLabel));
    }

    private void BuildDays(DateOnly today)
    {
        _builtFor = today;
        _window = new List<PrayerSlot>();
        if (_source is { } source)
        {
            foreach (var d in new[] { today.AddDays(-1), today, today.AddDays(1) })
                if (source.TimesFor(d) is { } times)
                    _window.AddRange(Schedule.Build(d, times, source.IqamaFor(d), TimeZoneInfo.Local));
        }
        _today = _window.Where(s => s.Date == today).ToList();
        _ledger.PruneBefore(today.AddDays(-2));
    }

    #endregion

    #region Tick, flyout rows, and alerts

    private void Tick()
    {
        var now = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(now.LocalDateTime);
        if (today != _builtFor) BuildDays(today);

        if (!_fetching && Config.Mosque is { Provider: MosqueProvider.Mawaqit }
            && (_backoff.IsDue(now) || (_backoff.Failures == 0 && _lastSuccess != DateTimeOffset.MinValue && now - _lastSuccess >= RefreshEvery)))
            _ = RefreshAsync(force: false);

        var next = Schedule.Next(_window, now, Config.Countdown);
        string name, time, countdown, tracking, headline;
        bool near;
        if (next is null)
        {
            name = "Mawaqit";
            time = "";
            countdown = _today.Count == 0 ? (HasError ? "error" : "…") : "--";
            tracking = "";
            headline = LocaleManager.Default.Get("UI.NoUpcoming");
            near = false;
        }
        else
        {
            var loc = LocaleManager.Default;
            var prayer = loc.Prayer(next.Kind.ToString());
            var phase = next.ToIqama ? loc.Get("UI.Iqama") : loc.Get("UI.Adhan");
            name = next.ToIqama ? $"{prayer} {phase}" : prayer;
            time = next.ToIqama ? next.Slot.IqamaDisplay ?? "" : next.Slot.AdhanDisplay;
            countdown = Schedule.FormatCountdown(next.Remaining);
            tracking = $"{prayer} {phase}";
            headline = $"{prayer} · {time}";
            near = Config.Notifications.MinutesBefore > 0
                   && next.Remaining.TotalMinutes <= Config.Notifications.MinutesBefore;
        }

        var changed = new List<string>();
        if (ChipName != name) { ChipName = name; changed.Add(nameof(ChipName)); }
        if (ChipTime != time) { ChipTime = time; changed.Add(nameof(ChipTime)); }
        if (ChipCountdown != countdown) { ChipCountdown = countdown; changed.Add(nameof(ChipCountdown)); }
        if (TrackingLabel != tracking) { TrackingLabel = tracking; changed.Add(nameof(TrackingLabel)); }
        if (NextHeadline != headline) { NextHeadline = headline; changed.Add(nameof(NextHeadline)); }
        if (IsNear != near) { IsNear = near; changed.Add(nameof(IsNear)); }
        if (changed.Count > 0)
        {
            changed.Add(nameof(ChipLine));
            Raise(changed.ToArray());
        }

        var progress = next is null ? 0 : Schedule.Progress(_window, now, next);
        if (Math.Abs(progress - NextProgress) > 0.002)
        {
            NextProgress = progress;
            Raise(nameof(NextProgress));
        }

        RebuildRows(next);
        RaiseAlerts(now);
    }

    private void RebuildRows(NextPrayer? next)
    {
        var fp = string.Join('|', _today.Select(s => $"{s.Kind}:{s.AdhanDisplay}:{s.IqamaDisplay}"))
                 + $":{next?.Slot.Date}:{next?.Kind}:{next?.ToIqama}:{LocaleManager.Default.Resolved}";
        if (fp == _slotsFingerprint) return;
        _slotsFingerprint = fp;
        Slots.Clear();
        foreach (var s in _today)
        {
            Slots.Add(new PrayerRow
            {
                Name = LocaleManager.Default.Prayer(s.Kind.ToString()),
                Adhan = s.AdhanDisplay,
                Iqama = s.IqamaDisplay ?? "—",
                IsNext = next != null && ReferenceEquals(s, next.Slot)
            });
        }
    }

    private void RaiseAlerts(DateTimeOffset now)
    {
        if (!Config.Notifications.Enabled || _window.Count == 0) return;
        var loc = LocaleManager.Default;
        foreach (var alert in Alerts.Pending(_window, now, Config.Notifications, _ledger).ToList())
        {
            _ledger.Mark(alert);
            var s = alert.Slot;
            var prayer = loc.Prayer(s.Kind.ToString());
            var (title, body) = alert.Phase switch
            {
                AlertPhase.Before => (string.Format(loc.Get("Alert.Before"), prayer, Config.Notifications.MinutesBefore),
                                      string.Format(loc.Get("Alert.BeforeBody"), s.AdhanDisplay)),
                AlertPhase.Iqama => (string.Format(loc.Get("Alert.Iqama"), prayer),
                                     string.Format(loc.Get("Alert.IqamaBody"), s.IqamaDisplay)),
                _ => (string.Format(loc.Get("Alert.Adhan"), prayer),
                      string.Format(loc.Get("Alert.AdhanBody"), s.AdhanDisplay)),
            };
            NotificationService.Show(title, body);
        }
    }

    #endregion

    #region Property change helpers

    private void RaiseDisplayMode()
    {
        var mode = ChipLayout.ToDisplayMode(Config.ChipSize);
        if (mode == _lastDisplayMode) return;
        _lastDisplayMode = mode;
        DisplayModeChanged?.Invoke(mode);
    }

    private void Raise(params string[] names)
    {
        foreach (var n in names)
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
    }

    #endregion
}
