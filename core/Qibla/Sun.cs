namespace Mawaqit.Core;

public readonly record struct SunPosition(double Azimuth, double Elevation);

/// <summary>Solar position from the NOAA solar calculator equations (accurate to well
/// under a degree for 1900–2100, which is plenty for orienting by the sun).</summary>
public static class Sun
{
    public static SunPosition Position(DateTimeOffset instant, double latitude, double longitude)
    {
        var (decl, eqTime) = Ephemeris(instant);
        var minutes = instant.UtcDateTime.TimeOfDay.TotalMinutes;
        var trueSolar = ((minutes + eqTime + 4 * longitude) % 1440 + 1440) % 1440;
        var hourAngle = trueSolar / 4 < 0 ? trueSolar / 4 + 180 : trueSolar / 4 - 180;

        var lat = Qibla.Rad(latitude);
        var dec = Qibla.Rad(decl);
        var cosZenith = Math.Clamp(
            Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(Qibla.Rad(hourAngle)), -1, 1);
        var zenith = Math.Acos(cosZenith);

        double azimuth;
        var denom = Math.Cos(lat) * Math.Sin(zenith);
        if (Math.Abs(denom) < 1e-9)
        {
            azimuth = latitude > 0 ? 180 : 0;
        }
        else
        {
            var a = Qibla.Deg(Math.Acos(Math.Clamp((Math.Sin(lat) * Math.Cos(zenith) - Math.Sin(dec)) / denom, -1, 1)));
            azimuth = hourAngle > 0 ? (a + 180) % 360 : (540 - a) % 360;
        }
        return new SunPosition(azimuth, 90 - Qibla.Deg(zenith));
    }

    /// <summary>Solar noon (sun due south/north) at <paramref name="longitude"/> on a UTC date.</summary>
    public static DateTimeOffset SolarNoon(DateOnly utcDate, double longitude)
    {
        var midnight = new DateTimeOffset(utcDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var noon = midnight.AddMinutes(720 - 4 * longitude);
        for (var i = 0; i < 2; i++)
            noon = midnight.AddMinutes(720 - 4 * longitude - Ephemeris(noon).EquationOfTimeMinutes);
        return noon;
    }

    /// <summary>Instants in <paramref name="year"/> when the sun stands directly over the
    /// point (closest daily solar noon). Only exists within the tropics.</summary>
    public static IReadOnlyList<DateTimeOffset> OverheadTransits(int year, double latitude, double longitude)
    {
        var found = new List<DateTimeOffset>();
        if (Math.Abs(latitude) > 23.5) return found;
        double? prevDiff = null;
        var prevNoon = default(DateTimeOffset);
        for (var day = new DateOnly(year, 1, 1).AddDays(-1); day <= new DateOnly(year, 12, 31).AddDays(1); day = day.AddDays(1))
        {
            var noon = SolarNoon(day, longitude);
            var diff = Ephemeris(noon).DeclinationDegrees - latitude;
            if (prevDiff is { } p && Math.Sign(p) != Math.Sign(diff))
                found.Add(Math.Abs(p) <= Math.Abs(diff) ? prevNoon : noon);
            prevDiff = diff;
            prevNoon = noon;
        }
        return found.Where(t => t.UtcDateTime.Year == year).ToList();
    }

    public static (double DeclinationDegrees, double EquationOfTimeMinutes) Ephemeris(DateTimeOffset instant)
    {
        var jd = instant.UtcDateTime.ToOADate() + 2415018.5;
        var jc = (jd - 2451545) / 36525;

        var meanLong = Qibla.Normalize360(280.46646 + jc * (36000.76983 + jc * 0.0003032));
        var meanAnom = 357.52911 + jc * (35999.05029 - 0.0001537 * jc);
        var eccent = 0.016708634 - jc * (0.000042037 + 0.0000001267 * jc);
        var m = Qibla.Rad(meanAnom);
        var center = Math.Sin(m) * (1.914602 - jc * (0.004817 + 0.000014 * jc))
                   + Math.Sin(2 * m) * (0.019993 - 0.000101 * jc)
                   + Math.Sin(3 * m) * 0.000289;
        var omega = Qibla.Rad(125.04 - 1934.136 * jc);
        var apparentLong = meanLong + center - 0.00569 - 0.00478 * Math.Sin(omega);
        var meanObliq = 23 + (26 + (21.448 - jc * (46.815 + jc * (0.00059 - jc * 0.001813))) / 60) / 60;
        var obliq = Qibla.Rad(meanObliq + 0.00256 * Math.Cos(omega));

        var decl = Qibla.Deg(Math.Asin(Math.Sin(obliq) * Math.Sin(Qibla.Rad(apparentLong))));

        var y = Math.Pow(Math.Tan(obliq / 2), 2);
        var l0 = Qibla.Rad(meanLong);
        var eqTime = 4 * Qibla.Deg(
            y * Math.Sin(2 * l0)
            - 2 * eccent * Math.Sin(m)
            + 4 * eccent * y * Math.Sin(m) * Math.Cos(2 * l0)
            - 0.5 * y * y * Math.Sin(4 * l0)
            - 1.25 * eccent * eccent * Math.Sin(2 * m));
        return (decl, eqTime);
    }
}
