using Tomlyn;
using Tomlyn.Model;

namespace Mawaqit.WinUI.Data;

public enum CountdownTarget
{
    Adhan,
    Iqama,
    AdhanThenIqama
}

public enum ChipSize
{
    Compact,
    Expanded
}

public enum HostKind
{
    Injected,
    Strip
}

public sealed class MosqueSelection
{
    public string MasjidId { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public sealed class NotificationPrefs
{
    public bool Enabled { get; set; } = true;
    public bool AtAdhan { get; set; } = true;
    public bool AtIqama { get; set; }
    public uint MinutesBefore { get; set; } = 10;
    public bool Fajr { get; set; } = true;
    public bool Dhuhr { get; set; } = true;
    public bool Asr { get; set; } = true;
    public bool Maghrib { get; set; } = true;
    public bool Isha { get; set; } = true;
    public bool Shuruq { get; set; }
    public bool SetupCompleted { get; set; }

    public bool PrayerEnabled(string name) => name switch
    {
        "Fajr" => Fajr,
        "Shuruq" => Shuruq,
        "Dhuhr" => Dhuhr,
        "Asr" => Asr,
        "Maghrib" => Maghrib,
        "Isha" => Isha,
        _ => false
    };
}

public sealed class AppConfig
{
    public const int MaxFavorites = 5;
    public const string DefaultApiBase = "http://127.0.0.1:8000";

    public string ApiBaseUrl { get; set; } = DefaultApiBase;
    public MosqueSelection? Mosque { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public NotificationPrefs Notifications { get; set; } = new();
    public CountdownTarget Countdown { get; set; } = CountdownTarget.Adhan;
    public List<MosqueSelection> Favorites { get; set; } = new();
    public int CustomPosition { get; set; } = -1;
    public string Display { get; set; } = "PRIMARY";
    public bool AutoAlignment { get; set; } = true;
    public ChipSize ChipSize { get; set; } = ChipSize.Expanded;
    public HostKind Host { get; set; } = HostKind.Injected;
    public string Language { get; set; } = "system";

    public bool IsFavorite(string masjidId)
        => Favorites.Any(f => f.MasjidId == masjidId);

    public string? AddFavorite(MosqueSelection sel)
    {
        if (string.IsNullOrWhiteSpace(sel.MasjidId)) return null;
        if (IsFavorite(sel.MasjidId)) return null;
        if (Favorites.Count >= MaxFavorites)
            return Helpers.LocaleManager.Default.Get("UI.FavoritesFull");
        Favorites.Add(sel);
        return null;
    }

    public void RemoveFavorite(string masjidId)
        => Favorites.RemoveAll(f => f.MasjidId == masjidId);
}

internal static class AppConfigStore
{
    /// <summary>Roaming config dir — matches Rust <c>directories</c> ProjectDirs("net","Mawaqit","Mawaqit Widget").</summary>
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mawaqit", "Mawaqit Widget");

    /// <summary>Local data (logs, cache) — same org/app names as Rust.</summary>
    public static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mawaqit", "Mawaqit Widget");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.toml");
    public static string CachePath => Path.Combine(RootDir, "cache", "cache.json");

    public static AppConfig Load()
    {
        if (File.Exists(ConfigPath))
            return Parse(File.ReadAllText(ConfigPath));

        // C# host briefly wrote here before matching the Rust roaming path.
        var localMisplaced = Path.Combine(RootDir, "config.toml");
        if (File.Exists(localMisplaced))
        {
            var migrated = Parse(File.ReadAllText(localMisplaced));
            Save(migrated);
            return migrated;
        }

        var legacy = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "mawaqit", "mawaqit-taskbar", "config.toml");
        if (File.Exists(legacy))
        {
            var migrated = Parse(File.ReadAllText(legacy));
            Save(migrated);
            return migrated;
        }

        return new AppConfig();
    }

    public static void Save(AppConfig cfg)
    {
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(ConfigPath, ToToml(cfg));
    }

    private static AppConfig Parse(string text)
    {
        var model = Toml.ToModel(text);
        var cfg = new AppConfig();
        cfg.ApiBaseUrl = Str(model, "api_base_url", cfg.ApiBaseUrl);
        cfg.StartWithWindows = Bool(model, "start_with_windows", true);
        cfg.CustomPosition = (int)Long(model, "custom_position", -1);
        cfg.Display = Str(model, "display", "PRIMARY");
        cfg.AutoAlignment = Bool(model, "auto_alignment", true);
        cfg.ChipSize = ParseChipSize(model);
        cfg.Host = ParseHost(model);
        cfg.Countdown = ParseCountdown(Str(model, "countdown", "adhan"));
        cfg.Language = Str(model, "language", "system");

        if (model.TryGetValue("mosque", out var mosqueObj) && mosqueObj is TomlTable mt)
            cfg.Mosque = ReadMosque(mt);

        if (model.TryGetValue("notifications", out var nObj) && nObj is TomlTable nt)
        {
            var n = cfg.Notifications;
            n.Enabled = Bool(nt, "enabled", true);
            n.AtAdhan = Bool(nt, "at_adhan", true);
            n.AtIqama = Bool(nt, "at_iqama", false);
            n.MinutesBefore = (uint)Long(nt, "minutes_before", 10);
            n.Fajr = Bool(nt, "fajr", true);
            n.Dhuhr = Bool(nt, "dhuhr", true);
            n.Asr = Bool(nt, "asr", true);
            n.Maghrib = Bool(nt, "maghrib", true);
            n.Isha = Bool(nt, "isha", true);
            n.Shuruq = Bool(nt, "shuruq", false);
            n.SetupCompleted = Bool(nt, "setup_completed", false);
        }

        if (model.TryGetValue("favorites", out var fObj) && fObj is TomlTableArray arr)
        {
            foreach (var row in arr)
            {
                var m = ReadMosque(row);
                if (!string.IsNullOrWhiteSpace(m.MasjidId))
                    cfg.Favorites.Add(m);
            }
        }
        return cfg;
    }

    private static MosqueSelection ReadMosque(TomlTable t) => new()
    {
        MasjidId = Str(t, "masjid_id", ""),
        DisplayName = Str(t, "display_name", "")
    };

    private static string ToToml(AppConfig cfg)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"api_base_url = \"{Esc(cfg.ApiBaseUrl)}\"");
        sb.AppendLine($"start_with_windows = {cfg.StartWithWindows.ToString().ToLowerInvariant()}");
        sb.AppendLine($"countdown = \"{CountdownToml(cfg.Countdown)}\"");
        sb.AppendLine($"custom_position = {cfg.CustomPosition}");
        sb.AppendLine($"display = \"{Esc(cfg.Display)}\"");
        sb.AppendLine($"auto_alignment = {cfg.AutoAlignment.ToString().ToLowerInvariant()}");
        sb.AppendLine($"chip_size = \"{cfg.ChipSize.ToString().ToLowerInvariant()}\"");
        sb.AppendLine($"host = \"{cfg.Host.ToString().ToLowerInvariant()}\"");
        sb.AppendLine($"language = \"{Esc(cfg.Language)}\"");
        if (cfg.Mosque is { } m)
        {
            sb.AppendLine();
            sb.AppendLine("[mosque]");
            sb.AppendLine($"masjid_id = \"{Esc(m.MasjidId)}\"");
            sb.AppendLine($"display_name = \"{Esc(m.DisplayName)}\"");
        }
        var n = cfg.Notifications;
        sb.AppendLine();
        sb.AppendLine("[notifications]");
        sb.AppendLine($"enabled = {n.Enabled.ToString().ToLowerInvariant()}");
        sb.AppendLine($"at_adhan = {n.AtAdhan.ToString().ToLowerInvariant()}");
        sb.AppendLine($"at_iqama = {n.AtIqama.ToString().ToLowerInvariant()}");
        sb.AppendLine($"minutes_before = {n.MinutesBefore}");
        sb.AppendLine($"fajr = {n.Fajr.ToString().ToLowerInvariant()}");
        sb.AppendLine($"dhuhr = {n.Dhuhr.ToString().ToLowerInvariant()}");
        sb.AppendLine($"asr = {n.Asr.ToString().ToLowerInvariant()}");
        sb.AppendLine($"maghrib = {n.Maghrib.ToString().ToLowerInvariant()}");
        sb.AppendLine($"isha = {n.Isha.ToString().ToLowerInvariant()}");
        sb.AppendLine($"shuruq = {n.Shuruq.ToString().ToLowerInvariant()}");
        sb.AppendLine($"setup_completed = {n.SetupCompleted.ToString().ToLowerInvariant()}");
        foreach (var fav in cfg.Favorites)
        {
            sb.AppendLine();
            sb.AppendLine("[[favorites]]");
            sb.AppendLine($"masjid_id = \"{Esc(fav.MasjidId)}\"");
            sb.AppendLine($"display_name = \"{Esc(fav.DisplayName)}\"");
        }
        return sb.ToString();
    }

    private static ChipSize ParseChipSize(TomlTable t)
    {
        var named = Str(t, "chip_size", "");
        if (named.Equals("compact", StringComparison.OrdinalIgnoreCase)) return ChipSize.Compact;
        if (Bool(t, "compact", false)) return ChipSize.Compact;
        return ChipSize.Expanded;
    }

    private static CountdownTarget ParseCountdown(string raw)
    {
        if (raw.Equals("iqama", StringComparison.OrdinalIgnoreCase)) return CountdownTarget.Iqama;
        if (raw.Equals("adhan_then_iqama", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("adhan-then-iqama", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("sequential", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("then", StringComparison.OrdinalIgnoreCase)
            || raw.Equals("adhantheniqama", StringComparison.OrdinalIgnoreCase))
            return CountdownTarget.AdhanThenIqama;
        return CountdownTarget.Adhan;
    }

    private static string CountdownToml(CountdownTarget t) => t switch
    {
        CountdownTarget.Iqama => "iqama",
        CountdownTarget.AdhanThenIqama => "adhan_then_iqama",
        _ => "adhan"
    };

    private static HostKind ParseHost(TomlTable t)
    {
        var named = Str(t, "host", "injected");
        if (named.Equals("strip", StringComparison.OrdinalIgnoreCase)
            || named.Equals("overlay", StringComparison.OrdinalIgnoreCase)
            || named.Equals("appbar", StringComparison.OrdinalIgnoreCase))
            return HostKind.Strip;
        return HostKind.Injected;
    }

    private static string Str(TomlTable t, string key, string fallback)
        => t.TryGetValue(key, out var v) && v is string s ? s : fallback;

    private static bool Bool(TomlTable t, string key, bool fallback)
        => t.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    private static long Long(TomlTable t, string key, long fallback)
        => t.TryGetValue(key, out var v) ? Convert.ToInt64(v) : fallback;

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}

internal static class HostPrefs
{
    public static HostKind Host
    {
        get => AppState.Current.Config.Host;
        set => AppState.Current.Config.Host = value;
    }

    public static string Display
    {
        get => AppState.Current.Config.Display;
        set => AppState.Current.Config.Display = value;
    }

    public static int CustomPosition
    {
        get => AppState.Current.Config.CustomPosition;
        set => AppState.Current.Config.CustomPosition = value;
    }

    public static bool AutoAlignment
    {
        get => AppState.Current.Config.AutoAlignment;
        set => AppState.Current.Config.AutoAlignment = value;
    }

    public static bool IsConfigured => AppState.Current.Config.Mosque != null;

    public static void Save() => AppState.Current.Persist();
}
