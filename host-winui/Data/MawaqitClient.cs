using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Mawaqit.WinUI.Data;

public sealed class PrayerTimes
{
    public string Fajr { get; set; } = "";
    public string Sunrise { get; set; } = "";
    public string Dohr { get; set; } = "";
    public string Asr { get; set; } = "";
    public string Maghreb { get; set; } = "";
    public string Icha { get; set; } = "";
}

public sealed class IqamaTimes
{
    public string Fajr { get; set; } = "";
    public string Dohr { get; set; } = "";
    public string Asr { get; set; } = "";
    public string Maghreb { get; set; } = "";
    public string Icha { get; set; } = "";
}

public sealed class MosqueHit
{
    public string Slug { get; set; } = "";
    public string Label { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
}

public sealed class MawaqitClient
{
    private readonly HttpClient _http;
    private readonly string _apiBase;
    private readonly Dictionary<string, List<MosqueHit>> _countryCache = new();

    public MawaqitClient(string apiBase)
    {
        _apiBase = apiBase.TrimEnd('/');
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation(
            "User-Agent", "MawaqitWidget/0.4 (Windows; +https://mawaqit.net) Mozilla/5.0");
    }

    public async Task<(PrayerTimes Times, IqamaTimes Iqama)> FetchDayAsync(string masjidId, DateTime date, CancellationToken ct = default)
    {
        PrayerTimes times;
        try { times = await PrayerTimesViaApiAsync(masjidId, ct); }
        catch
        {
            times = await TimesFromPageAsync(masjidId, ct);
        }
        var iqama = await IqamaForDayAsync(masjidId, date.Month, date.Day, ct);
        return (times, iqama);
    }

    private async Task<PrayerTimes> PrayerTimesViaApiAsync(string masjidId, CancellationToken ct)
    {
        var url = $"{_apiBase}/api/v1/{Uri.EscapeDataString(masjidId)}/prayer-times";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        return JsonSerializer.Deserialize<PrayerTimes>(json, JsonOpt())
               ?? throw new InvalidOperationException("empty prayer times");
    }

    private async Task<IqamaTimes> IqamaForDayAsync(string masjidId, int month, int day, CancellationToken ct)
    {
        try
        {
            var url = $"{_apiBase}/api/v1/{Uri.EscapeDataString(masjidId)}/calendar-iqama/{month}";
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            var rows = JsonSerializer.Deserialize<List<IqamaTimes>>(json, JsonOpt()) ?? new();
            var idx = Math.Max(0, day - 1);
            if (idx < rows.Count && HasIqama(rows[idx])) return rows[idx];
        }
        catch { /* fall through to page */ }
        return await IqamaFromPageAsync(masjidId, month, day, ct);
    }

    private async Task<JsonNode> FetchConfDataAsync(string slug, CancellationToken ct)
    {
        foreach (var lang in new[] { "en", "fr", "ar" })
        {
            var url = $"https://mawaqit.net/{lang}/{slug}";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode == 404) continue;
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct);
            var conf = ExtractConfData(html);
            if (conf != null) return conf;
        }
        throw new InvalidOperationException("page not found or confData missing");
    }

    private async Task<PrayerTimes> TimesFromPageAsync(string slug, CancellationToken ct)
    {
        var conf = await FetchConfDataAsync(slug, ct);
        return TimesFromConf(conf) ?? throw new InvalidOperationException("Could not read today's prayer times.");
    }

    private async Task<IqamaTimes> IqamaFromPageAsync(string slug, int month, int day, CancellationToken ct)
    {
        try
        {
            var conf = await FetchConfDataAsync(slug, ct);
            return IqamaFromConf(conf, month, day) ?? new IqamaTimes();
        }
        catch { return new IqamaTimes(); }
    }

    public async Task<string> ResolveNameAsync(string slug, CancellationToken ct = default)
    {
        var conf = await FetchConfDataAsync(slug, ct);
        return conf["name"]?.GetValue<string>() ?? slug;
    }

    public async Task<List<MosqueHit>> SearchInViewAsync(double lat, double lon, double? north, double? south, double? east, double? west, CancellationToken ct = default)
    {
        var cc = await CountryCodeAtAsync(lat, lon, ct);
        var country = await MosquesByCountryAsync(cc, ct);
        if (north is double n && south is double s && east is double e && west is double w)
        {
            var inView = country.Where(h =>
                h.Lat is double la && h.Lon is double lo &&
                la <= n && la >= s && lo <= e && lo >= w).ToList();
            if (inView.Count > 0)
            {
                inView.Sort((a, b) => Haversine(lat, lon, a).CompareTo(Haversine(lat, lon, b)));
                return inView.Take(60).ToList();
            }
        }
        return Nearest(country, lat, lon, 40);
    }

    public async Task<List<MosqueHit>> SearchMosquesAsync(string word, CancellationToken ct = default)
    {
        word = word.Trim();
        if (word.Length == 0) return new();
        if (ExtractSlugFromUrl(word) is string fromUrl)
        {
            var name = await ResolveNameAsync(fromUrl, ct);
            return new() { new MosqueHit { Slug = fromUrl, Label = name } };
        }
        try
        {
            var geo = await GeocodeAsync(word, ct);
            if (geo is { } g)
            {
                var country = await MosquesByCountryAsync(g.cc, ct);
                var hits = Filter(country, word);
                if (hits.Count == 0) hits = Nearest(country, g.lat, g.lon, 30);
                if (hits.Count > 0) return hits;
            }
        }
        catch { /* ignore */ }
        return new();
    }

    public async Task<List<MosqueHit>> MosquesByCountryAsync(string countryCode, CancellationToken ct)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        lock (_countryCache)
        {
            if (_countryCache.TryGetValue(code, out var cached)) return cached;
        }
        var url = $"https://mawaqit.net/api/2.0/mosque/map/{code}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("Accept", "application/json");
        req.Headers.TryAddWithoutValidation("Referer", "https://mawaqit.net/");
        req.Headers.TryAddWithoutValidation("Origin", "https://mawaqit.net");
        using var resp = await _http.SendAsync(req, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var hits = new List<MosqueHit>();
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            var slug = item.TryGetProperty("slug", out var sl) ? sl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(slug)) continue;
            var name = item.TryGetProperty("name", out var nm) ? nm.GetString() ?? slug : slug;
            var city = item.TryGetProperty("city", out var c) ? c.GetString() : null;
            var zip = item.TryGetProperty("zipcode", out var z) ? z.GetString() : null;
            var loc = string.Join(", ", new[] { city, zip }.Where(x => !string.IsNullOrEmpty(x)));
            double? lat = item.TryGetProperty("lat", out var la) && la.TryGetDouble(out var lav) ? lav : null;
            double? lon = item.TryGetProperty("lng", out var ln) && ln.TryGetDouble(out var lnv) ? lnv
                : item.TryGetProperty("lon", out var lo) && lo.TryGetDouble(out var lov) ? lov : null;
            hits.Add(new MosqueHit
            {
                Slug = slug,
                Label = string.IsNullOrEmpty(loc) ? name : $"{name} — {loc}",
                Lat = lat,
                Lon = lon
            });
        }
        lock (_countryCache) { _countryCache[code] = hits; }
        return hits;
    }

    private async Task<string> CountryCodeAtAsync(double lat, double lon, CancellationToken ct)
    {
        try
        {
            var url = $"https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=json";
            using var resp = await _http.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("address", out var addr) &&
                addr.TryGetProperty("country_code", out var cc))
                return cc.GetString()?.ToUpperInvariant() ?? GuessCountry(lat, lon);
        }
        catch { }
        return GuessCountry(lat, lon);
    }

    private async Task<(double lat, double lon, string cc)?> GeocodeAsync(string word, CancellationToken ct)
    {
        var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(word)}&format=json&limit=1";
        using var resp = await _http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.GetArrayLength() == 0) return null;
        var first = doc.RootElement[0];
        if (!double.TryParse(first.GetProperty("lat").GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(first.GetProperty("lon").GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon)) return null;
        var cc = await CountryCodeAtAsync(lat, lon, ct);
        return (lat, lon, cc);
    }

    public static string? NormalizeMosqueInput(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return null;
        if (ExtractSlugFromUrl(input) is string slug) return slug;
        if (!input.Contains(' ') && input.Length >= 3 && input.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
            return input.ToLowerInvariant();
        return null;
    }

    public static string? ExtractSlugFromUrl(string input)
    {
        var s = input.Trim().TrimEnd('/');
        var q = s.IndexOfAny(['?', '#']);
        if (q >= 0) s = s[..q].TrimEnd('/');
        var lower = s.ToLowerInvariant();
        var marker = "mawaqit.net/";
        var idx = lower.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0 && !lower.StartsWith("mawaqit.net/")) return null;
        if (idx < 0) idx = 0;
        else idx += marker.Length;
        var after = s[Math.Min(idx, s.Length)..];
        var parts = after.Split('/', StringSplitOptions.RemoveEmptyEntries);
        static bool Meta(string p) => p is "m" or "mobile" or "www" or "api" or "static"
            or "en" or "fr" or "ar" or "es" or "de" or "nl" or "tr" or "id" or "ur" or "bn" or "ru" or "zh" or "it" or "pt";
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            if (!Meta(parts[i].ToLowerInvariant()))
                return parts[i];
        }
        return null;
    }

    private static JsonSerializerOptions JsonOpt() => new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static JsonNode? ExtractConfData(string html)
    {
        foreach (var marker in new[] { "var confData =", "let confData =", "confData =" })
        {
            var start = html.IndexOf(marker, StringComparison.Ordinal);
            if (start < 0) continue;
            start += marker.Length;
            var slice = html[start..].TrimStart();
            if (!slice.StartsWith('{')) continue;
            int depth = 0, end = -1;
            for (int i = 0; i < slice.Length; i++)
            {
                if (slice[i] == '{') depth++;
                else if (slice[i] == '}')
                {
                    depth--;
                    if (depth == 0) { end = i + 1; break; }
                }
            }
            if (end < 0) continue;
            try { return JsonNode.Parse(slice[..end]); }
            catch { }
        }
        return null;
    }

    private static PrayerTimes? TimesFromConf(JsonNode conf)
    {
        var times = conf["times"]?.AsArray();
        if (times is null || times.Count < 5) return null;
        string Get(int i) => times[i]?.ToString() ?? "";
        return new PrayerTimes
        {
            Fajr = Get(0),
            Sunrise = conf["shuruq"]?.ToString() ?? "00:00",
            Dohr = Get(1),
            Asr = Get(2),
            Maghreb = Get(3),
            Icha = Get(4)
        };
    }

    private static IqamaTimes? IqamaFromConf(JsonNode conf, int month, int day)
    {
        var cal = conf["iqamaCalendar"]?.AsArray();
        if (cal != null)
        {
            var monthIdx = Math.Max(0, month - 1);
            if (monthIdx < cal.Count && cal[monthIdx] is JsonObject monthObj
                && monthObj[day.ToString()] is JsonArray row)
            {
                var iq = IqamaFromRow(row);
                if (iq != null) return iq;
            }
        }
        if (conf["iqama"] is JsonArray fixedRow)
            return IqamaFromRow(fixedRow);
        return null;
    }

    private static IqamaTimes? IqamaFromRow(JsonArray row)
    {
        if (row.Count < 5) return null;
        string Get(int i) => row[i]?.ToString() ?? "";
        var iq = new IqamaTimes { Fajr = Get(0), Dohr = Get(1), Asr = Get(2), Maghreb = Get(3), Icha = Get(4) };
        return HasIqama(iq) ? iq : null;
    }

    private static bool HasIqama(IqamaTimes iq)
        => new[] { iq.Fajr, iq.Dohr, iq.Asr, iq.Maghreb, iq.Icha }.Any(s => !string.IsNullOrWhiteSpace(s));

    private static List<MosqueHit> Filter(List<MosqueHit> all, string word)
    {
        var q = word.ToLowerInvariant();
        var tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => t.Length >= 2).ToArray();
        return all.Where(h =>
        {
            var hay = $"{h.Label} {h.Slug}".ToLowerInvariant();
            return hay.Contains(q) || (tokens.Length > 0 && tokens.All(t => hay.Contains(t)));
        }).Take(40).ToList();
    }

    private static List<MosqueHit> Nearest(List<MosqueHit> all, double lat, double lon, int limit)
        => all.Where(h => h.Lat.HasValue && h.Lon.HasValue)
            .OrderBy(h => Haversine(lat, lon, h))
            .Take(limit)
            .ToList();

    private static double Haversine(double lat1, double lon1, MosqueHit h)
    {
        const double R = 6371;
        var lat2 = h.Lat ?? 0;
        var lon2 = h.Lon ?? 0;
        static double Rad(double d) => d * Math.PI / 180;
        var dlat = Rad(lat2 - lat1);
        var dlon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dlat / 2), 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 2 * R * Math.Asin(Math.Sqrt(a));
    }

    private static string GuessCountry(double lat, double lon)
    {
        if (lat is >= 41 and <= 83 && lon is >= -141 and <= -52) return "CA";
        if (lat is >= 24 and <= 50 && lon is >= -125 and <= -66) return "US";
        if (lat is >= 41 and <= 51.5 && lon is >= -5.5 and <= 10) return "FR";
        if (lat is >= 49 and <= 61 && lon is >= -8.5 and <= 2) return "GB";
        if (lat is >= 47 and <= 55.5 && lon is >= 5.5 and <= 15.5) return "DE";
        return "FR";
    }
}
