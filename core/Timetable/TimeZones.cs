namespace Mawaqit.Core;

public static class TimeZones
{
    /// <summary>Wall-clock time in <paramref name="zone"/> to an instant. Times inside a
    /// spring-forward gap move to after it; fall-back repeats take the first occurrence.</summary>
    public static DateTimeOffset ToInstant(DateTime wall, TimeZoneInfo zone)
    {
        wall = DateTime.SpecifyKind(wall, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(wall)) wall = wall.AddHours(1);
        var offset = zone.IsAmbiguousTime(wall)
            ? zone.GetAmbiguousTimeOffsets(wall).Max()
            : zone.GetUtcOffset(wall);
        return new DateTimeOffset(wall, offset);
    }

    public static DateOnly DateIn(DateTimeOffset instant, TimeZoneInfo zone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);
}
