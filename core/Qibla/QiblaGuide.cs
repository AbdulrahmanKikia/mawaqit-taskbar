namespace Mawaqit.Core;

/// <summary>A sun moment that shows the Qibla with a shadow: when the sun is over the
/// Kaaba, shadows point away from it; over the antipode, shadows point towards it.</summary>
public sealed record ShadowMoment(DateTimeOffset At, bool ShadowPointsToQibla);

public sealed record QiblaReading(
    double Bearing,
    int CompassPoint,
    double DistanceKm,
    SunPosition Sun,
    // Turn from facing the sun to facing the Qibla (positive = right); null when the sun is too low.
    double? TurnFromSun,
    ShadowMoment? NextShadow);

public static class QiblaGuide
{
    /// <summary>Below this the sun is too low (or hidden) to orient by.</summary>
    public const double MinUsefulSunElevation = 5;

    public static QiblaReading Read(double latitude, double longitude, DateTimeOffset now)
    {
        var bearing = Qibla.Bearing(latitude, longitude);
        var sun = Sun.Position(now, latitude, longitude);
        double? turn = sun.Elevation >= MinUsefulSunElevation ? Qibla.TurnFrom(sun.Azimuth, bearing) : null;
        return new QiblaReading(
            bearing,
            Qibla.CompassPoint16(bearing),
            Qibla.DistanceKm(latitude, longitude),
            sun,
            turn,
            NextShadow(latitude, longitude, now));
    }

    /// <summary>Next moment within ~13 months when that sun event is visible from here.</summary>
    public static ShadowMoment? NextShadow(double latitude, double longitude, DateTimeOffset now)
    {
        var antiLat = -Qibla.KaabaLatitude;
        var antiLon = Qibla.KaabaLongitude - 180;
        var moments = new List<ShadowMoment>();
        for (var year = now.UtcDateTime.Year; year <= now.UtcDateTime.Year + 1; year++)
        {
            moments.AddRange(Sun.OverheadTransits(year, Qibla.KaabaLatitude, Qibla.KaabaLongitude)
                .Select(t => new ShadowMoment(t, ShadowPointsToQibla: false)));
            moments.AddRange(Sun.OverheadTransits(year, antiLat, antiLon)
                .Select(t => new ShadowMoment(t, ShadowPointsToQibla: true)));
        }
        return moments
            .Where(m => m.At > now)
            .Where(m => Sun.Position(m.At, latitude, longitude).Elevation >= MinUsefulSunElevation)
            .OrderBy(m => m.At)
            .FirstOrDefault();
    }
}
