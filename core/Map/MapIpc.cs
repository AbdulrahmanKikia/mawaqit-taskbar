using System.Text.Json;

namespace Mawaqit.Core;

public sealed class MapIpcException(string message) : Exception(message);

public sealed record GeoBounds(double North, double South, double East, double West);

public abstract record MapMessage;
public sealed record MapReady : MapMessage;
public sealed record MapNearby(double Lat, double Lon, GeoBounds? Bounds, int Seq) : MapMessage;
public sealed record MapSearch(string Word, int Seq) : MapMessage;
public sealed record MapSelect(string Slug, string Label, bool Favorite, double? Lat, double? Lon,
    MosqueProvider Provider = MosqueProvider.Mawaqit, string? Website = null) : MapMessage;
/// <summary>"Use this spot as my location" from the map.</summary>
public sealed record MapLocation(double Lat, double Lon, string Label) : MapMessage;

/// <summary>The schema for messages the map page posts to the host. Every message is
/// validated here before the host acts on it; anything malformed is rejected with a
/// <see cref="MapIpcException"/>, never half-used.</summary>
public static class MapIpc
{
    public const int MaxRawLength = 16 * 1024;
    public const int MaxWordLength = 200;
    public const int MaxLabelLength = 300;

    public static MapMessage Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) throw new MapIpcException("empty message");
        if (raw.Length > MaxRawLength) throw new MapIpcException("message too large");

        JsonDocument doc;
        try { doc = JsonDocument.Parse(raw); }
        catch (JsonException) { throw new MapIpcException("message is not JSON"); }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new MapIpcException("message is not an object");
            return String(root, "type") switch
            {
                "ready" => new MapReady(),
                "nearby" => new MapNearby(
                    Latitude(Number(root, "lat") ?? throw new MapIpcException("lat missing")),
                    WrapLongitude(Number(root, "lon") ?? throw new MapIpcException("lon missing")),
                    Bounds(root),
                    Seq(root)),
                "search" => new MapSearch(Word(root), Seq(root)),
                "select" => Select(root),
                "location" => new MapLocation(
                    Latitude(Number(root, "lat") ?? throw new MapIpcException("lat missing")),
                    WrapLongitude(Number(root, "lon") ?? throw new MapIpcException("lon missing")),
                    Clean(String(root, "label") ?? "", MaxLabelLength)),
                var other => throw new MapIpcException($"unknown message type '{other}'")
            };
        }
    }

    private static MapSelect Select(JsonElement root)
    {
        var slug = String(root, "slug");
        if (!MosqueSlug.IsValid(slug)) throw new MapIpcException("invalid mosque id");
        var label = Clean(String(root, "label") ?? slug!, MaxLabelLength);
        var favorite = root.TryGetProperty("favorite", out var f) && f.ValueKind == JsonValueKind.True;
        double? lat = Number(root, "lat"), lon = Number(root, "lon");
        if (lat is not (>= -90 and <= 90) || lon is null) { lat = null; lon = null; }
        else lon = WrapLongitude(lon.Value);
        var provider = String(root, "provider") == "osm" ? MosqueProvider.OpenStreetMap : MosqueProvider.Mawaqit;
        // Without a location an OpenStreetMap mosque has no times at all.
        if (provider == MosqueProvider.OpenStreetMap && lat is null) throw new MapIpcException("mosque location missing");
        string? website = null;
        if (Uri.TryCreate(String(root, "website") ?? "", UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            website = uri.ToString();
        return new MapSelect(slug!, label.Length == 0 ? slug! : label, favorite, lat, lon, provider, website);
    }

    private static string Word(JsonElement root)
    {
        var word = Clean(String(root, "word") ?? "", MaxWordLength + 1);
        if (word.Length == 0) throw new MapIpcException("search word missing");
        if (word.Length > MaxWordLength) throw new MapIpcException("search word too long");
        return word;
    }

    private static GeoBounds? Bounds(JsonElement root)
    {
        double? n = Number(root, "north"), s = Number(root, "south"), e = Number(root, "east"), w = Number(root, "west");
        if (n is null || s is null || e is null || w is null) return null;
        // A view that spans or wraps the antimeridian cannot be a simple box; use nearest instead.
        if (n > 90 || s < -90 || n < s || e > 180 || w < -180 || e < w) return null;
        return new GeoBounds(n.Value, s.Value, e.Value, w.Value);
    }

    private static int Seq(JsonElement root)
        => root.TryGetProperty("seq", out var v) && v.TryGetInt32(out var i) && i >= 0 ? i : 0;

    private static double Latitude(double lat)
        => lat is >= -90 and <= 90 ? lat : throw new MapIpcException("lat out of range");

    /// <summary>Leaflet reports longitudes past ±180 after panning around the world.</summary>
    internal static double WrapLongitude(double lon)
    {
        if (!double.IsFinite(lon)) throw new MapIpcException("lon is not a number");
        if (lon is >= -180 and <= 180) return lon; // already valid: keep it exact
        return ((lon + 180) % 360 + 360) % 360 - 180;
    }

    private static string? String(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double? Number(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d) && double.IsFinite(d)
            ? d
            : null;

    private static string Clean(string s, int max)
    {
        var chars = s.Where(c => !char.IsControl(c)).Take(max).ToArray();
        return new string(chars).Trim();
    }
}
