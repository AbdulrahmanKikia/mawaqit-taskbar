namespace Mawaqit.Core;

public enum PrayerKind { Fajr, Shuruq, Dhuhr, Asr, Maghrib, Isha }

/// <summary>One day of adhan times as the mosque publishes them (wall clock, "HH:mm").</summary>
public sealed record DayTimes(string Fajr, string Shuruq, string Dhuhr, string Asr, string Maghrib, string Isha)
{
    public string For(PrayerKind kind) => kind switch
    {
        PrayerKind.Fajr => Fajr,
        PrayerKind.Shuruq => Shuruq,
        PrayerKind.Dhuhr => Dhuhr,
        PrayerKind.Asr => Asr,
        PrayerKind.Maghrib => Maghrib,
        PrayerKind.Isha => Isha,
        _ => ""
    };
}

/// <summary>One day of iqama entries: "HH:mm", "+N" minutes after adhan, or empty.</summary>
public sealed record DayIqama(string Fajr, string Dhuhr, string Asr, string Maghrib, string Isha)
{
    public string? For(PrayerKind kind) => kind switch
    {
        PrayerKind.Fajr => Fajr,
        PrayerKind.Dhuhr => Dhuhr,
        PrayerKind.Asr => Asr,
        PrayerKind.Maghrib => Maghrib,
        PrayerKind.Isha => Isha,
        _ => null
    };

    public bool IsEmpty => new[] { Fajr, Dhuhr, Asr, Maghrib, Isha }.All(string.IsNullOrWhiteSpace);
}

/// <summary>Where a day's times come from: a mosque's published timetable or a calculation.</summary>
public interface ITimesSource
{
    DayTimes? TimesFor(DateOnly date);
    DayIqama? IqamaFor(DateOnly date);
}

/// <summary>
/// A mosque's yearly timetable, parsed from its Mawaqit page and cached on disk so the
/// widget keeps working offline. Days are keyed "MM-DD" because Mawaqit publishes a
/// 366-day template (Feb 29 included every year).
/// </summary>
public sealed class MosqueTimetable : ITimesSource
{
    public const int CurrentSchema = 1;

    public int Schema { get; set; } = CurrentSchema;
    public string Slug { get; set; } = "";
    public string Name { get; set; } = "";
    public string? TimeZoneId { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string? Jumua { get; set; }
    public string? Jumua2 { get; set; }
    public DateTimeOffset FetchedAt { get; set; }
    public Dictionary<string, DayTimes> Days { get; set; } = new();
    public Dictionary<string, DayIqama> Iqama { get; set; } = new();
    public DayIqama? DefaultIqama { get; set; }

    public static string Key(DateOnly date) => $"{date.Month:00}-{date.Day:00}";

    public DayTimes? TimesFor(DateOnly date) => Days.GetValueOrDefault(Key(date));

    public DayIqama? IqamaFor(DateOnly date)
        => Iqama.TryGetValue(Key(date), out var iq) ? iq : DefaultIqama;
}
