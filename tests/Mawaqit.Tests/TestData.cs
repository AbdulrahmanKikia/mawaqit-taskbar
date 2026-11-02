using System.Text.Json.Nodes;
using Mawaqit.Core;

namespace Mawaqit.Tests;

internal static class TestData
{
    /// <summary>When the fixtures were captured: 23:45 on 1 Nov in Montreal, 2 Nov in UTC.</summary>
    public static readonly DateTimeOffset CapturedAt = new(2026, 11, 2, 4, 45, 0, TimeSpan.Zero);

    public static TimeZoneInfo Zone(string iana) => TimeZoneInfo.FindSystemTimeZoneById(iana);

    public static JsonObject Conf(string name)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", name + ".confdata.json");
        return (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
    }

    public static (MosqueTimetable Table, ParseReport Report) Parse(string name, string zone, DateTimeOffset? at = null)
        => ConfData.Parse(name, Conf(name), at ?? CapturedAt, Zone(zone));
}
