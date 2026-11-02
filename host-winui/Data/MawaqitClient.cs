using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace Mawaqit.WinUI.Data;

public sealed class MosqueHit
{
    public string Slug { get; set; } = "";
    public string Label { get; set; } = "";
    public double? Lat { get; set; }
    public double? Lon { get; set; }
    public MosqueProvider Provider { get; set; } = MosqueProvider.Mawaqit;
    public string? Website { get; set; }
}

public sealed class MawaqitClient
{
    private readonly HttpClient _http = Http.Create(TimeSpan.FromSeconds(30));
    private readonly Dictionary<string, List<MosqueHit>> _countryCache = new();
    private readonly Dictionary<(double, double), string> _reverseCache = new();
    private readonly SemaphoreSlim _nominatimGate = new(1, 1);
    private DateTime _lastNominatimCall = DateTime.MinValue;

    /// <summary>One page fetch gives the whole year of adhan + iqama times.</summary>
    public async Task<(MosqueTimetable Table, ParseReport Report)> FetchTimetableAsync(string slug, CancellationToken ct = default)
    {
        var conf = await FetchConfDataAsync(slug, ct);
        var (table, report) = ConfData.Parse(slug, conf, DateTimeOffset.UtcNow, TimeZoneInfo.Local);
        Log.Information("Timetable {Slug}: {Days} days, {Iqama} iqama days, calendar trusted {Trusted}, live times = {Matched}",
            slug, report.DaysAccepted, report.IqamaDaysAccepted, report.CalendarTrusted, report.LiveTimesMatched);
        foreach (var skip in report.Skipped.Take(20))
            Log.Warning("Timetable {Slug} skipped {Reason}", slug, skip);
        foreach (var odd in report.Suspicious.Take(10))
            Log.Warning("Timetable {Slug} suspicious {Reason}", slug, odd);
        if (report.Skipped.Count > 20)
            Log.Warning("Timetable {Slug}: {More} more skipped entries", slug, report.Skipped.Count - 20);
        return (table, report);
    }

    private async Task<System.Text.Json.Nodes.JsonObject> FetchConfDataAsync(string slug, CancellationToken ct)
    {
        if (!MosqueSlug.IsValid(slug)) throw new InvalidOperationException($"'{slug}' is not a Mawaqit mosque id.");
        foreach (var lang in new[] { "en", "fr", "ar" })
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://mawaqit.net/{lang}/{slug}");
            req.Headers.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml");
            using var resp = await _http.SendAsync(req, ct);
            if ((int)resp.StatusCode == 404) continue;
            resp.EnsureSuccessStatusCode();
            var html = await resp.Content.ReadAsStringAsync(ct);
            if (ConfData.Extract(html) is { } conf) return conf;
        }
        throw new InvalidOperationException("Mosque page not found, or it has no timetable.");
    }

    public async Task<string> ResolveNameAsync(string slug, CancellationToken ct = default)
    {
        var conf = await FetchConfDataAsync(slug, ct);
        return conf["name"]?.GetValue<string>() ?? slug;
    }

    /// <summary>Mawaqit mosques in view (fast). OpenStreetMap ones come separately via <see cref="OsmInViewAsync"/>.</summary>
    public async Task<List<MosqueHit>> SearchInViewAsync(double lat, double lon, GeoBounds? bounds, CancellationToken ct = default)
    {
        var cc = await CountryCodeAtAsync(lat, lon, ct);
        if (cc is null) return new();
        var country = await MosquesByCountryAsync(cc, ct);
        var hits = bounds is { } b ? InBounds(country, b) : Nearest(country, lat, lon, 40);
        hits.Sort((x, y) => Distance(lat, lon, x).CompareTo(Distance(lat, lon, y)));
        return hits.Take(80).ToList();
    }

    /// <summary>OpenStreetMap mosques in view that are not already in <paramref name="mawaqit"/>.</summary>
    public async Task<List<MosqueHit>> OsmInViewAsync(GeoBounds bounds, IReadOnlyList<MosqueHit> mawaqit, CancellationToken ct = default)
        => WithoutDuplicates(mawaqit, InBounds(await OsmMosquesAsync(bounds, ct), bounds));

    public async Task<List<MosqueHit>> SearchMosquesAsync(string word, CancellationToken ct = default)
    {
        word = word.Trim();
        if (word.Length == 0) return new();
        if (MosqueSlug.FromUrl(word) is string fromUrl)
        {
            var name = await ResolveNameAsync(fromUrl, ct);
            return new() { new MosqueHit { Slug = fromUrl, Label = name } };
        }
        if (await GeocodeAsync(word, ct) is not { } g) return new();
        var country = await MosquesByCountryAsync(g.cc, ct);
        var around = new GeoBounds(g.lat + 0.2, g.lat - 0.2, g.lon + 0.3, g.lon - 0.3);
        var mawaqit = InBounds(country, around);
        var all = mawaqit.Concat(WithoutDuplicates(mawaqit, InBounds(await OsmMosquesAsync(around, ct), around))).ToList();
        var hits = Filter(Filter(country, word).Concat(all).DistinctBy(h => h.Slug).ToList(), word);
        return hits.Count > 0 ? hits : Nearest(all.Count > 0 ? all : country, g.lat, g.lon, 30);
    }

    /// <summary>Best effort: a town or city name for a point, for labels like "Brampton".</summary>
    public async Task<string?> PlaceNameAsync(GeoPoint at, CancellationToken ct = default)
    {
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://nominatim.openstreetmap.org/reverse?lat={at.Latitude}&lon={at.Longitude}&format=json&zoom=10");
        using var doc = await NominatimAsync(url, ct);
        if (doc?.RootElement.TryGetProperty("address", out var a) != true) return null;
        foreach (var key in new[] { "city", "town", "village", "municipality", "county", "state" })
            if (a.TryGetProperty(key, out var v) && v.GetString() is { Length: > 0 } name) return name;
        return null;
    }

    // ---- OpenStreetMap mosques ---------------------------------------------------------

    private sealed record OsmArea(double South, double West, double North, double East, bool Complete, DateTimeOffset SavedAt, List<MosqueHit> Hits)
    {
        public bool Covers(GeoBounds b) => South <= b.South && West <= b.West && North >= b.North && East >= b.East;
        /// <summary>Overpass results stay good for a month; keyword fallbacks are retried the next day.</summary>
        public bool Fresh(DateTimeOffset now) => now - SavedAt < (Complete ? TimeSpan.FromDays(30) : TimeSpan.FromDays(1));
    }

    private static readonly string[] OverpassServers =
    [
        "https://overpass-api.de/api/interpreter",
        "https://maps.mail.ru/osm/tools/overpass/api/interpreter",
    ];

    /// <summary>Names Nominatim can find mosques by when Overpass is down.</summary>
    private static readonly string[] MosqueWords = ["mosque", "masjid", "islamic", "jame", "musalla"];

    private List<OsmArea>? _osmAreas;
    private readonly SemaphoreSlim _osmGate = new(1, 1);
    private static string OsmCachePath => Path.Combine(AppConfigStore.CacheDir, "osm-mosques.json");

    /// <summary>
    /// OpenStreetMap mosques around a city-sized box: from the disk cache when we have the area,
    /// else Overpass (complete), else Nominatim keyword search (partial). Never throws: this
    /// layer is extra coverage and must not hold up the Mawaqit results.
    /// </summary>
    private async Task<List<MosqueHit>> OsmMosquesAsync(GeoBounds view, CancellationToken ct)
    {
        if (view.North - view.South > 1.2 || view.East - view.West > 1.8) return new(); // country views: too big
        await _osmGate.WaitAsync(ct);
        try
        {
            var areas = _osmAreas ??= LoadOsmAreas();
            var now = DateTimeOffset.UtcNow;
            if (areas.FirstOrDefault(a => a.Covers(view) && a.Fresh(now)) is { } hit) return hit.Hits;

            // Fetch a slightly larger, grid-snapped area so small pans hit the cache.
            var area = new GeoBounds(
                Math.Ceiling((view.North + 0.05) * 10) / 10, Math.Floor((view.South - 0.05) * 10) / 10,
                Math.Ceiling((view.East + 0.05) * 10) / 10, Math.Floor((view.West - 0.05) * 10) / 10);
            var (hits, complete) = await OverpassAsync(area, ct) is { } full ? (full, true) : (await NominatimMosquesAsync(area, ct), false);
            if (hits.Count == 0 && !complete) return hits;

            areas.RemoveAll(a => !a.Fresh(now) || (complete && area.South <= a.South && area.West <= a.West && area.North >= a.North && area.East >= a.East));
            areas.Add(new OsmArea(area.South, area.West, area.North, area.East, complete, now, hits));
            if (areas.Count > 60) areas.RemoveRange(0, areas.Count - 60);
            AtomicFile.WriteAllText(OsmCachePath, JsonSerializer.Serialize(areas));
            return hits;
        }
        finally
        {
            _osmGate.Release();
        }
    }

    private static List<OsmArea> LoadOsmAreas()
    {
        try
        {
            return File.Exists(OsmCachePath)
                ? JsonSerializer.Deserialize<List<OsmArea>>(File.ReadAllText(OsmCachePath)) ?? new()
                : new();
        }
        catch (JsonException ex)
        {
            Log.Warning(ex, "Ignoring unreadable OpenStreetMap cache");
            return new();
        }
    }

    private async Task<List<MosqueHit>?> OverpassAsync(GeoBounds b, CancellationToken ct)
    {
        var query = string.Create(CultureInfo.InvariantCulture,
            $"[out:json][timeout:10];nwr[\"amenity\"=\"place_of_worship\"][\"religion\"=\"muslim\"]({b.South},{b.West},{b.North},{b.East});out center tags 400;");
        foreach (var server in OverpassServers)
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(12));
                using var req = new HttpRequestMessage(HttpMethod.Post, server)
                {
                    Content = new FormUrlEncodedContent([new KeyValuePair<string, string>("data", query)])
                };
                using var resp = await _http.SendAsync(req, cts.Token);
                if (!resp.IsSuccessStatusCode)
                {
                    Log.Information("Overpass {Server} returned {Status}", server, (int)resp.StatusCode);
                    continue;
                }
                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cts.Token));
                var hits = new List<MosqueHit>();
                foreach (var e in doc.RootElement.GetProperty("elements").EnumerateArray())
                {
                    if (!e.TryGetProperty("tags", out var tags) || Prop(tags, "name") is not { Length: > 0 } name) continue;
                    var center = e.TryGetProperty("center", out var c) ? c : e;
                    if (!center.TryGetProperty("lat", out var la) || !center.TryGetProperty("lon", out var lo)) continue;
                    hits.Add(OsmHit(e.GetProperty("type").GetString(), e.GetProperty("id").GetInt64(), name,
                        Prop(tags, "addr:city"), la.GetDouble(), lo.GetDouble(), Prop(tags, "website") ?? Prop(tags, "contact:website")));
                }
                return hits;
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or KeyNotFoundException)
            {
                if (ct.IsCancellationRequested) throw;
                Log.Information("Overpass {Server} failed: {Message}", server, ex.Message);
            }
        }
        return null;
    }

    private async Task<List<MosqueHit>> NominatimMosquesAsync(GeoBounds b, CancellationToken ct)
    {
        var hits = new Dictionary<string, MosqueHit>();
        foreach (var word in MosqueWords)
        {
            var url = string.Create(CultureInfo.InvariantCulture,
                $"https://nominatim.openstreetmap.org/search?q={word}&viewbox={b.West},{b.North},{b.East},{b.South}&bounded=1&limit=50&format=jsonv2&addressdetails=1&extratags=1");
            using var doc = await NominatimAsync(url, ct);
            if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array) continue;
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (Prop(e, "type") != "place_of_worship" || Prop(e, "name") is not { Length: > 0 } name) continue;
                var hasLat = double.TryParse(Prop(e, "lat"), NumberStyles.Float, CultureInfo.InvariantCulture, out var la);
                var hasLon = double.TryParse(Prop(e, "lon"), NumberStyles.Float, CultureInfo.InvariantCulture, out var lo);
                if (!hasLat || !hasLon || !e.TryGetProperty("osm_id", out var id)) continue;
                var city = e.TryGetProperty("address", out var a) ? Prop(a, "city") ?? Prop(a, "town") : null;
                var site = e.TryGetProperty("extratags", out var x) && x.ValueKind == JsonValueKind.Object ? Prop(x, "website") : null;
                var hit = OsmHit(Prop(e, "osm_type"), id.GetInt64(), name, city, la, lo, site);
                hits.TryAdd(hit.Slug, hit);
            }
        }
        Log.Information("Overpass unavailable; found {Count} mosques via Nominatim keywords", hits.Count);
        return hits.Values.ToList();
    }

    private static MosqueHit OsmHit(string? osmType, long id, string name, string? city, double lat, double lon, string? website) => new()
    {
        Slug = $"osm-{osmType switch { "node" => "n", "way" => "w", _ => "r" }}{id}",
        Label = string.IsNullOrEmpty(city) ? name : $"{name} — {city}",
        Lat = lat,
        Lon = lon,
        Provider = MosqueProvider.OpenStreetMap,
        Website = Uri.TryCreate(website, UriKind.Absolute, out var u) && u.Scheme is "http" or "https" ? u.ToString() : null,
    };

    private static string? Prop(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static List<MosqueHit> InBounds(IEnumerable<MosqueHit> hits, GeoBounds b)
        => hits.Where(h => h.Lat is double la && h.Lon is double lo &&
                           la <= b.North && la >= b.South && lo <= b.East && lo >= b.West).ToList();

    /// <summary>OpenStreetMap entries minus those that are a Mawaqit mosque already (within 150 m).</summary>
    private static List<MosqueHit> WithoutDuplicates(IReadOnlyList<MosqueHit> mawaqit, IEnumerable<MosqueHit> osm)
        => osm.Where(o => !mawaqit.Any(m => m.Lat is { } la && m.Lon is { } lo && o.Lat is { } ola && o.Lon is { } olo
                                            && Qibla.GreatCircleKm(la, lo, ola, olo) < 0.15))
              .DistinctBy(o => o.Slug)
              .ToList();

    public async Task<List<MosqueHit>> MosquesByCountryAsync(string countryCode, CancellationToken ct)
    {
        var code = countryCode.Trim().ToUpperInvariant();
        lock (_countryCache)
        {
            if (_countryCache.TryGetValue(code, out var cached)) return cached;
        }
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://mawaqit.net/api/2.0/mosque/map/{Uri.EscapeDataString(code)}");
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
            if (!MosqueSlug.IsValid(slug)) continue;
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

    private async Task<string?> CountryCodeAtAsync(double lat, double lon, CancellationToken ct)
    {
        // Panning within a region should not hit Nominatim every time (~50 km grid).
        var key = (Math.Round(lat * 2) / 2, Math.Round(lon * 2) / 2);
        lock (_reverseCache)
        {
            if (_reverseCache.TryGetValue(key, out var hit)) return hit;
        }
        var url = string.Create(CultureInfo.InvariantCulture,
            $"https://nominatim.openstreetmap.org/reverse?lat={lat}&lon={lon}&format=json&zoom=3");
        using var doc = await NominatimAsync(url, ct);
        if (doc?.RootElement.TryGetProperty("address", out var addr) == true
            && addr.TryGetProperty("country_code", out var cc)
            && cc.GetString() is { Length: 2 } code)
        {
            code = code.ToUpperInvariant();
            lock (_reverseCache) { _reverseCache[key] = code; }
            return code;
        }
        return null;
    }

    private async Task<(double lat, double lon, string cc)?> GeocodeAsync(string word, CancellationToken ct)
    {
        using var doc = await NominatimAsync(
            $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(word)}&format=json&limit=1&addressdetails=1", ct);
        if (doc is null || doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0) return null;
        var first = doc.RootElement[0];
        if (!double.TryParse(first.GetProperty("lat").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)) return null;
        if (!double.TryParse(first.GetProperty("lon").GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var lon)) return null;
        var cc = first.TryGetProperty("address", out var addr) && addr.TryGetProperty("country_code", out var c)
            ? c.GetString()?.ToUpperInvariant()
            : await CountryCodeAtAsync(lat, lon, ct);
        return cc is { Length: 2 } ? (lat, lon, cc) : null;
    }

    /// <summary>Nominatim's usage policy: at most one request per second, identified client.</summary>
    private async Task<JsonDocument?> NominatimAsync(string url, CancellationToken ct)
    {
        await _nominatimGate.WaitAsync(ct);
        try
        {
            var wait = _lastNominatimCall.AddSeconds(1.1) - DateTime.UtcNow;
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
            _lastNominatimCall = DateTime.UtcNow;
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                Log.Warning("Nominatim returned {Status} for {Url}", (int)resp.StatusCode, url);
                return null;
            }
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        }
        finally
        {
            _nominatimGate.Release();
        }
    }

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
            .OrderBy(h => Distance(lat, lon, h))
            .Take(limit)
            .ToList();

    private static double Distance(double lat, double lon, MosqueHit h)
        => Qibla.GreatCircleKm(lat, lon, h.Lat ?? 0, h.Lon ?? 0);
}
