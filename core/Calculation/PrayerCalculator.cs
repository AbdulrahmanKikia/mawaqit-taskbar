using System.Globalization;

namespace Mawaqit.Core;

public readonly record struct GeoPoint(double Latitude, double Longitude);

/// <summary>Computed prayer instants for one local day; null where the sun never gets there.</summary>
public sealed record PrayerInstants(
    DateTimeOffset? Fajr,
    DateTimeOffset? Sunrise,
    DateTimeOffset Dhuhr,
    DateTimeOffset? Asr,
    DateTimeOffset? Maghrib,
    DateTimeOffset? Isha);

/// <summary>
/// Astronomical prayer times (the PrayTimes.org algorithm that api.aladhan.com also uses),
/// fully offline. Checked against Aladhan for 15 cities × 5 dates in the tests.
/// </summary>
public static class PrayerCalculator
{
    private const double RiseSetAngle = 0.833; // refraction + solar radius
    private static readonly UmAlQuraCalendar Hijri = new();

    public static PrayerInstants Compute(
        DateOnly date, GeoPoint at, CalculationMethod method, AsrSchool asr, HighLatitudeRule highLat)
    {
        var p = Methods.Params(method);
        var lat = at.Latitude;
        var lon = at.Longitude;
        var jDate = JulianDate(date) - lon / (15 * 24.0);

        // One refinement pass from rough local solar times, as PrayTimes does.
        double Day(double hours) => hours / 24;
        var fajr = SunAngleTime(jDate, lat, p.FajrAngle, Day(5), ccw: true);
        var sunrise = SunAngleTime(jDate, lat, RiseSetAngle, Day(6), ccw: true);
        var dhuhr = MidDay(jDate, Day(12));
        var asrT = AsrTime(jDate, lat, asr == AsrSchool.Hanafi ? 2 : 1, Day(13));
        var sunset = SunAngleTime(jDate, lat, RiseSetAngle, Day(18), ccw: false);
        var maghrib = p.MaghribAngle is { } ma ? SunAngleTime(jDate, lat, ma, Day(18), ccw: false) : sunset;
        var isha = p.IshaAngle is { } ia ? SunAngleTime(jDate, lat, ia, Day(18), ccw: false) : double.NaN;

        // Bound Fajr/Isha (and angle-based Maghrib) where twilight lasts all night.
        if (!double.IsNaN(sunrise) && !double.IsNaN(sunset))
        {
            var night = FixHour(sunrise - sunset);
            fajr = AdjustHighLat(fajr, sunrise, p.FajrAngle, night, ccw: true, highLat);
            if (p.IshaAngle is { } ia2) isha = AdjustHighLat(isha, sunset, ia2, night, ccw: false, highLat);
            if (p.MaghribAngle is { } ma2) maghrib = AdjustHighLat(maghrib, sunset, ma2, night, ccw: false, highLat);
        }

        maghrib += p.MaghribMinutes / 60;
        if (p.IshaMinutes is { } im)
        {
            var minutes = p.RamadanIshaMinutes is { } rm && IsRamadan(date) ? rm : im;
            isha = maghrib + minutes / 60;
        }

        // Hours above are local solar time; shift to UTC on this calendar date.
        var midnightUtc = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        DateTimeOffset? Instant(double hours)
            => double.IsNaN(hours) ? null : RoundToMinute(midnightUtc.AddHours(hours - lon / 15));

        var o = p.Offsets ?? [0, 0, 0, 0, 0, 0];
        return new PrayerInstants(
            Instant(fajr + o[0] / 60.0),
            Instant(sunrise + o[1] / 60.0),
            Instant(dhuhr + o[2] / 60.0)!.Value,
            Instant(asrT + o[3] / 60.0),
            Instant(maghrib + o[4] / 60.0),
            Instant(isha + o[5] / 60.0));
    }

    /// <summary>The day as wall-clock "HH:mm" in <paramref name="zone"/>, or null if any
    /// prayer cannot be computed (polar day/night) so the widget shows no wrong times.</summary>
    public static DayTimes? ToDayTimes(PrayerInstants t, TimeZoneInfo zone)
    {
        if (t.Fajr is not { } f || t.Sunrise is not { } s || t.Asr is not { } a || t.Maghrib is not { } m || t.Isha is not { } i)
            return null;
        string Hm(DateTimeOffset x) => TimeZoneInfo.ConvertTime(x, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
        return new DayTimes(Hm(f), Hm(s), Hm(t.Dhuhr), Hm(a), Hm(m), Hm(i));
    }

    public static bool IsRamadan(DateOnly date)
    {
        var d = date.ToDateTime(new TimeOnly(12, 0));
        return d >= Hijri.MinSupportedDateTime && d <= Hijri.MaxSupportedDateTime && Hijri.GetMonth(d) == 9;
    }

    // ---- PrayTimes.org core -------------------------------------------------------

    private static double SunAngleTime(double jDate, double lat, double angle, double time, bool ccw)
    {
        var (decl, _) = SunPosition(jDate + time);
        var noon = MidDay(jDate, time);
        var cos = (-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat));
        if (cos is < -1 or > 1) return double.NaN; // the sun never reaches this angle today
        var t = Deg(Math.Acos(cos)) / 15;
        return noon + (ccw ? -t : t);
    }

    private static double AsrTime(double jDate, double lat, double factor, double time)
    {
        var (decl, _) = SunPosition(jDate + time);
        var angle = -Deg(Math.Atan(1 / (factor + Math.Tan(Rad(Math.Abs(lat - decl))))));
        return SunAngleTime(jDate, lat, angle, time, ccw: false);
    }

    private static double MidDay(double jDate, double time)
    {
        var (_, eqt) = SunPosition(jDate + time);
        return FixHour(12 - eqt);
    }

    private static double AdjustHighLat(double time, double baseTime, double angle, double night, bool ccw, HighLatitudeRule rule)
    {
        var portion = rule switch
        {
            HighLatitudeRule.MiddleOfNight => night / 2,
            HighLatitudeRule.OneSeventh => night / 7,
            _ => angle / 60 * night,
        };
        var diff = ccw ? FixHour(baseTime - time) : FixHour(time - baseTime);
        return double.IsNaN(time) || diff > portion ? baseTime + (ccw ? -portion : portion) : time;
    }

    private static (double Declination, double EquationOfTime) SunPosition(double jd)
    {
        var d = jd - 2451545.0;
        var g = FixAngle(357.529 + 0.98560028 * d);
        var q = FixAngle(280.459 + 0.98564736 * d);
        var l = FixAngle(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g));
        var e = 23.439 - 0.00000036 * d;
        var ra = Deg(Math.Atan2(Cos(e) * Sin(l), Cos(l))) / 15;
        var eqt = q / 15 - FixHour(ra);
        var decl = Deg(Math.Asin(Sin(e) * Sin(l)));
        return (decl, eqt);
    }

    private static double JulianDate(DateOnly date)
    {
        int y = date.Year, m = date.Month, d = date.Day;
        if (m <= 2) { y--; m += 12; }
        var a = Math.Floor(y / 100.0);
        var b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
    }

    private static DateTimeOffset RoundToMinute(DateTimeOffset t)
    {
        var ticks = (t.UtcTicks + TimeSpan.TicksPerMinute / 2) / TimeSpan.TicksPerMinute * TimeSpan.TicksPerMinute;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static double Rad(double d) => d * Math.PI / 180;
    private static double Deg(double r) => r * 180 / Math.PI;
    private static double Sin(double d) => Math.Sin(Rad(d));
    private static double Cos(double d) => Math.Cos(Rad(d));
    private static double FixAngle(double a) => a - 360 * Math.Floor(a / 360);
    private static double FixHour(double h) => h - 24 * Math.Floor(h / 24);
}

/// <summary>Times computed on demand for a point; plugs into the same schedule as a mosque timetable.</summary>
public sealed class CalculatedTimes(GeoPoint at, CalculationMethod method, AsrSchool asr, HighLatitudeRule highLat, TimeZoneInfo zone, DayIqama? iqama = null)
    : ITimesSource
{
    private readonly Dictionary<DateOnly, DayTimes?> _cache = new();

    public GeoPoint At { get; } = at;
    public CalculationMethod Method { get; } = method;
    public AsrSchool Asr { get; } = asr;

    public DayTimes? TimesFor(DateOnly date)
    {
        if (!_cache.TryGetValue(date, out var day))
            _cache[date] = day = PrayerCalculator.ToDayTimes(PrayerCalculator.Compute(date, At, Method, Asr, highLat), zone);
        return day;
    }

    public DayIqama? IqamaFor(DateOnly date) => iqama;
}

/// <summary>A mosque timetable with calculated times for any day it lacks (not published,
/// or rejected by validation), so the widget never goes blank.</summary>
public sealed class FallbackTimes(ITimesSource primary, ITimesSource? fallback) : ITimesSource
{
    public DayTimes? TimesFor(DateOnly date) => primary.TimesFor(date) ?? fallback?.TimesFor(date);

    /// <summary>Iqama only belongs with the mosque's own times.</summary>
    public DayIqama? IqamaFor(DateOnly date)
        => primary.TimesFor(date) is not null ? primary.IqamaFor(date) : fallback?.IqamaFor(date);

    public bool UsesFallback(DateOnly date) => primary.TimesFor(date) is null && fallback?.TimesFor(date) is not null;
}
