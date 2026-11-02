using System.Text.Json;

namespace Mawaqit.Core;

/// <summary>Offline cache: one JSON timetable per mosque under the cache directory.</summary>
public sealed class TimetableStore(string directory)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string Directory { get; } = directory;

    public string? PathFor(string slug)
        => MosqueSlug.IsValid(slug) ? Path.Combine(Directory, slug + ".json") : null;

    /// <summary>Returns null when there is no usable cache (missing, other schema, corrupt).</summary>
    public MosqueTimetable? Load(string slug)
    {
        if (PathFor(slug) is not { } path || !File.Exists(path)) return null;
        try
        {
            var table = JsonSerializer.Deserialize<MosqueTimetable>(File.ReadAllText(path), Json);
            if (table is null || table.Schema != MosqueTimetable.CurrentSchema || table.Slug != slug || table.Days.Count == 0)
                return null;
            return table;
        }
        catch (JsonException)
        {
            // A torn or hand-edited cache is just a cache miss; the next refresh rewrites it.
            return null;
        }
    }

    public void Save(MosqueTimetable table)
    {
        var path = PathFor(table.Slug) ?? throw new ArgumentException($"Unsafe mosque id '{table.Slug}'", nameof(table));
        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(table, Json));
    }
}
