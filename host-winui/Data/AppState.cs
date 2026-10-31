using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Input;
using Microsoft.UI.Dispatching;
using Mawaqit.WinUI.Helpers;
using Mawaqit.WinUI.Views;
using Serilog;

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
    public const string Version = "0.2.0";
    public const string Repo = "AbdulrahmanKikia/mawaqit-taskbar";

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

    private readonly MawaqitClient _publicMap = new("https://mawaqit.net");
    private MawaqitClient _client = new(AppConfig.DefaultApiBase);
    private DispatcherQueue? _dispatcher;
    private DispatcherQueueTimer? _tick;
    private IReadOnlyList<PrayerSlot> _today = Array.Empty<PrayerSlot>();
    private readonly HashSet<string> _notified = new();
    private DateTime _lastFetch = DateTime.MinValue;
    private bool _fetching;
    private string _slotsFingerprint = "";
    private DisplayMode _lastDisplayMode = DisplayMode.Expanded;

    public string ChipName { get; private set; } = "Mawaqit";
    public string ChipTime { get; private set; } = "";
    public string ChipCountdown { get; private set; } = "…";
    public string ChipLine => Config.ChipSize == ChipSize.Compact
        ? ChipCountdown
        : $"{ChipName}  {ChipTime}  ·  {ChipCountdown}";
    public string MosqueLabel => Config.Mosque?.DisplayName ?? "No mosque selected";
    public string TrackingLabel { get; private set; } = "";
    public string NextHeadline { get; private set; } = "";
    public string Status { get; private set; } = "loading";
    public bool HasError { get; private set; }
    public bool IsRefreshing { get; private set; }
    public bool IsConfigured => Config.Mosque != null;
    public bool IsNear { get; private set; }

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

    public void Load()
    {
        Config = AppConfigStore.Load();
        _client = new MawaqitClient(Config.ApiBaseUrl);
        _lastDisplayMode = ChipLayout.ToDisplayMode(Config.ChipSize);
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

    public void Start()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _tick = _dispatcher.CreateTimer();
        _tick.Interval = TimeSpan.FromSeconds(1);
        _tick.Tick += (_, _) => Tick();
        _tick.Start();
        _ = RefreshAsync(force: true);
        if (Config.Mosque is null)
            Views.MapWindow.Show();
        else if (!Config.Notifications.SetupCompleted)
            Views.SettingsWindow.Show();
    }

    public async Task ApplyMosqueAsync(string slug, string label, bool favorite)
    {
        Config.Mosque = new MosqueSelection { MasjidId = slug, DisplayName = label };
        if (favorite)
        {
            var err = Config.AddFavorite(new MosqueSelection { MasjidId = slug, DisplayName = label });
            if (err != null) MessageBox.Warning(err);
        }
        Persist();
        await RefreshAsync(force: true);
    }

    public async Task SwitchFavoriteAsync(MosqueSelection sel)
    {
        Config.Mosque = new MosqueSelection { MasjidId = sel.MasjidId, DisplayName = sel.DisplayName };
        Persist();
        await RefreshAsync(force: true);
    }

    public MawaqitClient MapClient => _publicMap;

    public async Task RefreshAsync(bool force)
    {
        if (_fetching) return;
        if (!force && DateTime.UtcNow - _lastFetch < TimeSpan.FromMinutes(45)) return;
        var mosque = Config.Mosque;
        if (mosque is null)
        {
            Status = "Choose a mosque";
            ChipName = "Mawaqit";
            ChipCountdown = "setup";
            TrackingLabel = "";
            NextHeadline = "";
            Raise(nameof(Status), nameof(ChipName), nameof(ChipCountdown), nameof(ChipLine),
                nameof(TrackingLabel), nameof(NextHeadline));
            return;
        }

        _fetching = true;
        IsRefreshing = true;
        HasError = false;
        Raise(nameof(IsRefreshing), nameof(HasError));
        try
        {
            var now = DateTime.Now;
            var (times, iqama) = await _client.FetchDayAsync(mosque.MasjidId, now);
            _today = Schedule.Build(now.Date, times, iqama);
            _lastFetch = DateTime.UtcNow;
            Status = mosque.DisplayName;
            Tick();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "refresh failed");
            HasError = true;
            Status = ex.Message;
            ChipCountdown = "error";
            Raise(nameof(HasError), nameof(Status), nameof(ChipCountdown), nameof(ChipLine),
                nameof(TrackingLabel), nameof(NextHeadline));
        }
        finally
        {
            _fetching = false;
            IsRefreshing = false;
            Raise(nameof(IsRefreshing));
        }
    }

    private void Tick()
    {
        if (_today.Count == 0)
        {
            RebuildRows(null);
            return;
        }
        var now = DateTime.Now;
        var next = Schedule.Next(_today, now, Config.Countdown);
        string name;
        string time;
        string countdown;
        string tracking;
        string headline;
        bool near;
        if (next is null)
        {
            name = "Mawaqit";
            time = "";
            countdown = "--";
            tracking = "";
            headline = "No upcoming prayer";
            near = false;
        }
        else
        {
            var phase = next.ToIqama ? "iqama" : "adhan";
            name = next.ToIqama ? $"{next.Kind} iqama" : next.Kind.ToString();
            time = Schedule.FormatHhmm(next.At.TimeOfDay);
            countdown = Schedule.FormatCountdown(next.Remaining);
            tracking = $"Tracking {next.Kind} {phase}";
            headline = $"Next: {next.Kind} at {time}";
            near = next.Remaining.TotalMinutes <= Config.Notifications.MinutesBefore
                   && Config.Notifications.MinutesBefore > 0;
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

        RebuildRows(next);
        RaiseAlerts(now);
        if (now.Date != _lastFetch.ToLocalTime().Date && _lastFetch != DateTime.MinValue)
            _ = RefreshAsync(force: true);
    }

    private void RebuildRows(NextPrayer? next)
    {
        var fp = string.Join('|', _today.Select(s => $"{s.Kind}:{s.AdhanDisplay}:{s.IqamaDisplay}"))
                 + $":{next?.Kind}:{next?.ToIqama}";
        if (fp == _slotsFingerprint) return;
        _slotsFingerprint = fp;
        Slots.Clear();
        foreach (var s in _today)
        {
            Slots.Add(new PrayerRow
            {
                Name = s.Label,
                Adhan = s.AdhanDisplay,
                Iqama = s.IqamaDisplay ?? "—",
                IsNext = next != null && s.Kind == next.Kind
            });
        }
    }

    private void RaiseAlerts(DateTime now)
    {
        if (!Config.Notifications.Enabled || _today.Count == 0) return;
        foreach (var alert in Alerts.Pending(now.Date, _today, now, Config.Notifications, _notified))
        {
            _notified.Add(alert.Key);
            NotificationService.Show(alert.Title, alert.Body);
        }
        if (_notified.Count > 80)
            _notified.Clear();
    }

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
}
