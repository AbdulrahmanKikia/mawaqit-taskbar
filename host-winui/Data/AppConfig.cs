using Serilog;

namespace Mawaqit.WinUI.Data;

internal static class AppConfigStore
{
    /// <summary>Roaming config dir — matches Rust <c>directories</c> ProjectDirs("net","Mawaqit","Mawaqit Widget").</summary>
    public static string ConfigDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mawaqit", "Mawaqit Widget");

    /// <summary>Local data (logs, cache, WebView2 profile) — same org/app names as Rust.</summary>
    public static string RootDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mawaqit", "Mawaqit Widget");

    public static string ConfigPath => Path.Combine(ConfigDir, "config.toml");
    public static string CacheDir => Path.Combine(RootDir, "cache");
    public static string WebViewDataDir => Path.Combine(RootDir, "webview2");

    /// <summary>Set when the config could not be read and was moved aside, so the UI can say so.</summary>
    public static string? QuarantinedPath { get; private set; }

    public static AppConfig Load()
    {
        foreach (var path in Candidates())
        {
            if (!File.Exists(path)) continue;
            try
            {
                var cfg = ConfigToml.Parse(File.ReadAllText(path));
                if (path != ConfigPath) Save(cfg);
                return cfg;
            }
            catch (ConfigCorruptException ex)
            {
                QuarantinedPath = AtomicFile.QuarantineCorrupt(path, DateTimeOffset.Now);
                Log.Error(ex, "Config at {Path} was unreadable; moved to {Moved} and starting fresh", path, QuarantinedPath);
                return new AppConfig();
            }
        }
        return new AppConfig();
    }

    public static void Save(AppConfig cfg) => AtomicFile.WriteAllText(ConfigPath, ConfigToml.Serialize(cfg));

    private static IEnumerable<string> Candidates()
    {
        yield return ConfigPath;
        // C# host briefly wrote here before matching the Rust roaming path.
        yield return Path.Combine(RootDir, "config.toml");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "mawaqit", "mawaqit-taskbar", "config.toml");
    }
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
