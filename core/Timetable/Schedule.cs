namespace Mawaqit.Core;

public enum CountdownTarget
{
    Adhan,
    Iqama,
    AdhanThenIqama
}

/// <summary>One prayer on one calendar day, resolved to real instants.</summary>
public sealed class PrayerSlot
{
    public required PrayerKind Kind { get; init; }
    /// <summary>The timetable day this slot was published under (Isha after midnight still belongs to it).</summary>
    public required DateOnly Date { get; init; }
    public required DateTimeOffset AdhanAt { get; init; }
    public required string AdhanDisplay { get; init; }
    public DateTimeOffset? IqamaAt { get; init; }
    public string? IqamaDisplay { get; init; }
}

public sealed record NextPrayer(PrayerSlot Slot, DateTimeOffset At, TimeSpan Remaining, bool ToIqama)
{
    public PrayerKind Kind => Slot.Kind;
}

public static class Schedule
{
    public static IReadOnlyList<PrayerSlot> Build(DateOnly date, DayTimes times, DayIqama? iqama, TimeZoneInfo zone)
    {
        var slots = new List<PrayerSlot>();
        var midnight = date.ToDateTime(TimeOnly.MinValue);
        TimeSpan? previous = null;
        var dayOffset = TimeSpan.Zero;

        foreach (var kind in Enum.GetValues<PrayerKind>())
        {
            if (!TryParseHhmm(times.For(kind), out var t)) continue;
            // Published times only go forward; an earlier clock time means it is after midnight.
            if (previous is { } p && t < p) dayOffset = TimeSpan.FromDays(1);
            previous = t;
            var adhanWall = midnight + dayOffset + t;

            DateTimeOffset? iqamaAt = null;
            string? iqamaDisplay = null;
            var raw = iqama?.For(kind)?.Trim();
            if (!string.IsNullOrEmpty(raw))
            {
                if (ResolveIqama(adhanWall, raw) is { } iqWall)
                {
                    iqamaAt = TimeZones.ToInstant(iqWall, zone);
                    iqamaDisplay = FormatHhmm(iqWall.TimeOfDay);
                }
                else
                {
                    iqamaDisplay = raw;
                }
            }

            slots.Add(new PrayerSlot
            {
                Kind = kind,
                Date = date,
                AdhanAt = TimeZones.ToInstant(adhanWall, zone),
                AdhanDisplay = FormatHhmm(t),
                IqamaAt = iqamaAt,
                IqamaDisplay = iqamaDisplay,
            });
        }
        return slots;
    }

    /// <summary>First adhan/iqama after <paramref name="now"/>. Pass yesterday, today and
    /// tomorrow in order so after-midnight Isha and tomorrow's Fajr are both covered.</summary>
    public static NextPrayer? Next(IEnumerable<PrayerSlot> chronological, DateTimeOffset now, CountdownTarget target)
    {
        foreach (var slot in chronological)
        {
            if (slot.Kind == PrayerKind.Shuruq) continue;
            foreach (var (at, toIqama) in Candidates(slot, target))
                if (at > now) return new NextPrayer(slot, at, at - now, toIqama);
        }
        return null;
    }

    /// <summary>Share of the way from the last adhan before <paramref name="now"/> to <paramref name="next"/>, 0..1.</summary>
    public static double Progress(IEnumerable<PrayerSlot> chronological, DateTimeOffset now, NextPrayer next)
    {
        var previous = chronological
            .Where(s => s.Kind != PrayerKind.Shuruq)
            .SelectMany(s => s.IqamaAt is { } iq ? new[] { s.AdhanAt, iq } : new[] { s.AdhanAt })
            .Where(t => t <= now)
            .DefaultIfEmpty(next.At - TimeSpan.FromHours(6))
            .Max();
        var span = (next.At - previous).TotalSeconds;
        return span <= 0 ? 0 : Math.Clamp((now - previous).TotalSeconds / span, 0, 1);
    }

    public static string FormatCountdown(TimeSpan remaining)
    {
        var total = Math.Max(0, (long)remaining.TotalSeconds);
        var h = total / 3600;
        var m = total % 3600 / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public static string FormatHhmm(TimeSpan t) => $"{t.Hours:00}:{t.Minutes:00}";

    public static bool TryParseHhmm(string? raw, out TimeSpan time)
    {
        time = default;
        if (!ConfData.TryNormalizeTime(raw, out var normalized)) return false;
        time = TimeSpan.ParseExact(normalized, @"hh\:mm", System.Globalization.CultureInfo.InvariantCulture);
        return true;
    }

    private static IEnumerable<(DateTimeOffset at, bool toIqama)> Candidates(PrayerSlot slot, CountdownTarget target)
    {
        switch (target)
        {
            case CountdownTarget.Iqama:
                yield return slot.IqamaAt is { } iq ? (iq, true) : (slot.AdhanAt, false);
                break;
            case CountdownTarget.AdhanThenIqama:
                yield return (slot.AdhanAt, false);
                if (slot.IqamaAt is { } iq2 && iq2 > slot.AdhanAt) yield return (iq2, true);
                break;
            default:
                yield return (slot.AdhanAt, false);
                break;
        }
    }

    private static DateTime? ResolveIqama(DateTime adhanWall, string raw)
    {
        if (raw.StartsWith('+'))
            return int.TryParse(raw.AsSpan(1), out var mins) && mins is >= 0 and <= 180
                ? adhanWall.AddMinutes(mins)
                : null;
        if (!TryParseHhmm(raw, out var t)) return null;
        var wall = adhanWall.Date + t;
        if (wall >= adhanWall) return wall;
        // Isha at 23:30 with iqama at 00:10 is the next day; anything else is a data error.
        return adhanWall - wall > TimeSpan.FromHours(12) ? wall.AddDays(1) : null;
    }
}
