using System.Globalization;
using System.Text;
using Tomlyn;
using Tomlyn.Model;

namespace Mawaqit.Core;

public sealed class ConfigCorruptException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>config.toml ⇄ <see cref="AppConfig"/>. Same keys as the Rust v0.1.8 build;
/// unknown keys are ignored and wrong-typed values fall back to defaults.</summary>
public static class ConfigToml
{
    /// <summary>The old default that pointed every install at a local API nobody runs.</summary>
    internal const string LegacyDefaultApiBase = "http://127.0.0.1:8000";

    public static AppConfig Parse(string text)
    {
        TomlTable model;
        try
        {
            model = Toml.ToModel(text);
        }
        catch (TomlException ex)
        {
            throw new ConfigCorruptException("config.toml is not valid TOML", ex);
        }

        var cfg = new AppConfig();
        var api = Str(model, "api_base_url", "");
        cfg.ApiBaseUrl = api.TrimEnd('/') == LegacyDefaultApiBase ? "" : api;
        cfg.StartWithWindows = Bool(model, "start_with_windows", true);
        cfg.CustomPosition = (int)Math.Clamp(Long(model, "custom_position", -1), -1, 100_000);
        cfg.Display = Str(model, "display", "PRIMARY");
        cfg.AutoAlignment = Bool(model, "auto_alignment", true);
        cfg.ChipSize = ParseChipSize(model);
        cfg.Host = ParseHost(model);
        cfg.Countdown = ParseCountdown(Str(model, "countdown", "adhan"));
        cfg.Language = Str(model, "language", "system");
        cfg.Times = Enum<TimesSource>(Str(model, "times_source", "mosque")) ?? TimesSource.Mosque;

        if (model.TryGetValue("calculation", out var cObj) && cObj is TomlTable ct)
        {
            cfg.Calculation.Method = Enum<CalculationMethod>(Str(ct, "method", "auto"));
            cfg.Calculation.Asr = Enum<AsrSchool>(Str(ct, "asr", "auto"));
            cfg.Calculation.HighLatitude = Enum<HighLatitudeRule>(Str(ct, "high_latitude", "angle_based")) ?? HighLatitudeRule.AngleBased;
        }

        if (model.TryGetValue("location", out var lObj) && lObj is TomlTable lt)
        {
            cfg.Location.Auto = Bool(lt, "auto", true);
            cfg.Location.Name = Str(lt, "name", "");
            if (Double(lt, "lat") is >= -90 and <= 90 && Double(lt, "lon") is >= -180 and <= 180)
            {
                cfg.Location.Latitude = Double(lt, "lat");
                cfg.Location.Longitude = Double(lt, "lon");
            }
        }

        if (model.TryGetValue("mosque", out var mosqueObj) && mosqueObj is TomlTable mt
            && ReadMosque(mt) is { } mosque)
            cfg.Mosque = mosque;

        if (model.TryGetValue("notifications", out var nObj) && nObj is TomlTable nt)
        {
            var n = cfg.Notifications;
            n.Enabled = Bool(nt, "enabled", true);
            n.AtAdhan = Bool(nt, "at_adhan", true);
            n.AtIqama = Bool(nt, "at_iqama", false);
            n.MinutesBefore = (uint)Math.Clamp(Long(nt, "minutes_before", 10), 0, AppConfig.MaxMinutesBefore);
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
                if (ReadMosque(row) is { } fav && !cfg.IsFavorite(fav.MasjidId) && cfg.Favorites.Count < AppConfig.MaxFavorites)
                    cfg.Favorites.Add(fav);
            }
        }
        return cfg;
    }

    public static string Serialize(AppConfig cfg)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"api_base_url = {Quote(cfg.ApiBaseUrl)}");
        sb.AppendLine($"start_with_windows = {B(cfg.StartWithWindows)}");
        sb.AppendLine($"countdown = {Quote(CountdownToml(cfg.Countdown))}");
        sb.AppendLine($"custom_position = {cfg.CustomPosition.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"display = {Quote(cfg.Display)}");
        sb.AppendLine($"auto_alignment = {B(cfg.AutoAlignment)}");
        sb.AppendLine($"chip_size = {Quote(cfg.ChipSize.ToString().ToLowerInvariant())}");
        sb.AppendLine($"host = {Quote(cfg.Host.ToString().ToLowerInvariant())}");
        sb.AppendLine($"language = {Quote(cfg.Language)}");
        sb.AppendLine($"times_source = {Quote(Name(cfg.Times))}");
        if (cfg.Mosque is { } m)
        {
            sb.AppendLine();
            sb.AppendLine("[mosque]");
            WriteMosque(sb, m);
        }
        sb.AppendLine();
        sb.AppendLine("[calculation]");
        sb.AppendLine($"method = {Quote(cfg.Calculation.Method is { } cm ? Name(cm) : "auto")}");
        sb.AppendLine($"asr = {Quote(cfg.Calculation.Asr is { } ca ? Name(ca) : "auto")}");
        sb.AppendLine($"high_latitude = {Quote(Name(cfg.Calculation.HighLatitude))}");

        var loc = cfg.Location;
        sb.AppendLine();
        sb.AppendLine("[location]");
        sb.AppendLine($"auto = {B(loc.Auto)}");
        sb.AppendLine($"name = {Quote(loc.Name)}");
        if (loc.Point is { } lp)
        {
            sb.AppendLine($"lat = {lp.Latitude.ToString("R", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"lon = {lp.Longitude.ToString("R", CultureInfo.InvariantCulture)}");
        }

        var n = cfg.Notifications;
        sb.AppendLine();
        sb.AppendLine("[notifications]");
        sb.AppendLine($"enabled = {B(n.Enabled)}");
        sb.AppendLine($"at_adhan = {B(n.AtAdhan)}");
        sb.AppendLine($"at_iqama = {B(n.AtIqama)}");
        sb.AppendLine($"minutes_before = {n.MinutesBefore.ToString(CultureInfo.InvariantCulture)}");
        sb.AppendLine($"fajr = {B(n.Fajr)}");
        sb.AppendLine($"dhuhr = {B(n.Dhuhr)}");
        sb.AppendLine($"asr = {B(n.Asr)}");
        sb.AppendLine($"maghrib = {B(n.Maghrib)}");
        sb.AppendLine($"isha = {B(n.Isha)}");
        sb.AppendLine($"shuruq = {B(n.Shuruq)}");
        sb.AppendLine($"setup_completed = {B(n.SetupCompleted)}");
        foreach (var fav in cfg.Favorites)
        {
            sb.AppendLine();
            sb.AppendLine("[[favorites]]");
            WriteMosque(sb, fav);
        }
        return sb.ToString();
    }

    private static void WriteMosque(StringBuilder sb, MosqueSelection m)
    {
        sb.AppendLine($"masjid_id = {Quote(m.MasjidId)}");
        sb.AppendLine($"display_name = {Quote(m.DisplayName)}");
        if (m.HasLocation)
        {
            sb.AppendLine($"lat = {m.Latitude!.Value.ToString("R", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"lon = {m.Longitude!.Value.ToString("R", CultureInfo.InvariantCulture)}");
        }
        if (m.Provider != MosqueProvider.Mawaqit)
            sb.AppendLine($"provider = {Quote(Name(m.Provider))}");
        if (!string.IsNullOrWhiteSpace(m.Website))
            sb.AppendLine($"website = {Quote(m.Website)}");
        if (m.Iqama is { IsEmpty: false } iq)
            sb.AppendLine($"iqama = [{string.Join(", ", new[] { iq.Fajr, iq.Dhuhr, iq.Asr, iq.Maghrib, iq.Isha }.Select(Quote))}]");
    }

    private static MosqueSelection? ReadMosque(TomlTable t)
    {
        var id = Str(t, "masjid_id", "").Trim();
        if (id.Length == 0) return null;
        var sel = new MosqueSelection { MasjidId = id, DisplayName = Str(t, "display_name", "") };
        var lat = Double(t, "lat");
        var lon = Double(t, "lon");
        if (lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
        {
            sel.Latitude = lat;
            sel.Longitude = lon;
        }
        sel.Provider = Enum<MosqueProvider>(Str(t, "provider", "mawaqit")) ?? MosqueProvider.Mawaqit;
        var site = Str(t, "website", "").Trim();
        if (Uri.TryCreate(site, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            sel.Website = uri.ToString();
        if (t.TryGetValue("iqama", out var iqObj) && iqObj is TomlArray arr && arr.Count == 5)
            sel.Iqama = Iqama.Parse(arr.Select(x => x as string ?? "").ToArray());
        return sel;
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
        var k = raw.Replace("-", "_").ToLowerInvariant();
        return k switch
        {
            "iqama" => CountdownTarget.Iqama,
            "adhan_then_iqama" or "sequential" or "then" or "adhantheniqama" => CountdownTarget.AdhanThenIqama,
            _ => CountdownTarget.Adhan
        };
    }

    private static string CountdownToml(CountdownTarget t) => t switch
    {
        CountdownTarget.Iqama => "iqama",
        CountdownTarget.AdhanThenIqama => "adhan_then_iqama",
        _ => "adhan"
    };

    private static HostKind ParseHost(TomlTable t)
        => Str(t, "host", "injected").ToLowerInvariant() is "strip" or "overlay" or "appbar"
            ? HostKind.Strip
            : HostKind.Injected;

    /// <summary>snake_case TOML name ⇄ enum (e.g. "high_latitude" style: angle_based, umm_al_qura).</summary>
    internal static string Name<T>(T value) where T : struct, System.Enum
        => string.Concat(value.ToString()!.Select((c, i) => i > 0 && char.IsUpper(c) ? "_" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));

    internal static T? Enum<T>(string raw) where T : struct, System.Enum
        => System.Enum.GetValues<T>().Cast<T?>().FirstOrDefault(v => Name(v!.Value) == raw.Trim().ToLowerInvariant());

    private static string Str(TomlTable t, string key, string fallback)
        => t.TryGetValue(key, out var v) && v is string s ? s : fallback;

    private static bool Bool(TomlTable t, string key, bool fallback)
        => t.TryGetValue(key, out var v) && v is bool b ? b : fallback;

    private static long Long(TomlTable t, string key, long fallback)
        => t.TryGetValue(key, out var v) ? v switch
        {
            long l => l,
            double d when double.IsFinite(d) => (long)d,
            _ => fallback
        } : fallback;

    private static double? Double(TomlTable t, string key)
        => t.TryGetValue(key, out var v) ? v switch
        {
            double d when double.IsFinite(d) => d,
            long l => l,
            _ => null
        } : null;

    private static string B(bool b) => b ? "true" : "false";

    /// <summary>TOML basic string. Control characters are escaped so a mosque name with a
    /// newline cannot break the file.</summary>
    internal static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default:
                    if (char.IsControl(c)) sb.Append($"\\u{(int)c:X4}");
                    else sb.Append(c);
                    break;
            }
        }
        return sb.Append('"').ToString();
    }
}
