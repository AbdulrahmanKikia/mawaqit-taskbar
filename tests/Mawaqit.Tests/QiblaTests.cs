using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class QiblaTests
{
    [Theory]
    [InlineData(51.5074, -0.1278, 119.0)]  // London
    [InlineData(48.8566, 2.3522, 119.2)]   // Paris
    [InlineData(45.5017, -73.5673, 58.7)]  // Montreal
    [InlineData(40.7128, -74.0060, 58.5)]  // New York
    [InlineData(-33.8688, 151.2093, 277.5)] // Sydney
    [InlineData(-6.2088, 106.8456, 295.1)] // Jakarta
    [InlineData(24.7136, 46.6753, 244.0)]  // Riyadh
    public void Bearing_matches_published_values(double lat, double lon, double expected)
        => Assert.InRange(Qibla.Bearing(lat, lon), expected - 0.5, expected + 0.5);

    [Fact]
    public void Bearing_matches_published_values_to_a_tenth()
    {
        Assert.InRange(Qibla.Bearing(51.5074, -0.1278), 118.9, 119.1);
        Assert.InRange(Qibla.Bearing(45.5017, -73.5673), 58.6, 58.8);
    }

    [Fact]
    public void Distance_london_to_makkah_is_about_4790_km()
        => Assert.InRange(Qibla.DistanceKm(51.5074, -0.1278), 4770, 4810);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(11.2, 0)]
    [InlineData(11.3, 1)]
    [InlineData(119, 5)]   // ESE
    [InlineData(359, 0)]
    public void Compass_points(double bearing, int point) => Assert.Equal(point, Qibla.CompassPoint16(bearing));

    [Theory]
    [InlineData(100, 119, 19)]
    [InlineData(350, 10, 20)]
    [InlineData(10, 350, -20)]
    [InlineData(0, 180, 180)]
    public void Turn_is_signed_shortest(double from, double to, double turn)
        => Assert.Equal(turn, Qibla.TurnFrom(from, to), precision: 6);

    [Fact]
    public void Sun_is_overhead_the_kaaba_on_the_published_qibla_days()
    {
        // Published: ~27/28 May 12:18 and ~15/16 July 12:27 Makkah time (UTC+3).
        var transits = Sun.OverheadTransits(2026, Qibla.KaabaLatitude, Qibla.KaabaLongitude);
        Assert.Equal(2, transits.Count);

        Assert.InRange(transits[0].UtcDateTime.Date, new DateTime(2026, 5, 27), new DateTime(2026, 5, 28));
        Assert.InRange(transits[0].UtcDateTime.TimeOfDay, new TimeSpan(9, 14, 0), new TimeSpan(9, 22, 0));
        Assert.InRange(transits[1].UtcDateTime.Date, new DateTime(2026, 7, 15), new DateTime(2026, 7, 16));
        Assert.InRange(transits[1].UtcDateTime.TimeOfDay, new TimeSpan(9, 23, 0), new TimeSpan(9, 31, 0));
    }

    [Theory]
    // Expected values from an independent algorithm (Astronomical Almanac low-precision formulas).
    [InlineData("2026-06-21T18:00:00Z", 40.015, -105.2705, 136.60, 68.76)]  // Boulder
    [InlineData("2026-06-21T12:00:00Z", 51.5074, -0.1278, 178.86, 61.92)]   // London
    [InlineData("2026-01-15T03:00:00Z", -33.8688, 151.2093, 312.51, 72.32)] // Sydney
    public void Sun_position_matches_an_independent_algorithm(string utc, double lat, double lon, double az, double el)
    {
        var p = Sun.Position(DateTimeOffset.Parse(utc, System.Globalization.CultureInfo.InvariantCulture), lat, lon);
        Assert.InRange(p.Azimuth, az - 0.5, az + 0.5);
        Assert.InRange(p.Elevation, el - 0.5, el + 0.5);
    }

    [Fact]
    public void Sun_is_over_the_kaabas_antipode_in_january_and_november()
    {
        // Published: ~13 Jan and ~28 Nov (around 21:00–21:30 UTC); shadows then point to the Qibla.
        var t = Sun.OverheadTransits(2027, -Qibla.KaabaLatitude, Qibla.KaabaLongitude - 180);
        Assert.Equal(2, t.Count);
        Assert.InRange(t[0].UtcDateTime.Date, new DateTime(2027, 1, 12), new DateTime(2027, 1, 14));
        Assert.InRange(t[1].UtcDateTime.Date, new DateTime(2027, 11, 27), new DateTime(2027, 11, 29));
        Assert.All(t, x => Assert.InRange(x.UtcDateTime.Hour, 20, 21));
    }

    [Fact]
    public void Sun_is_straight_up_over_the_kaaba_at_the_computed_moment()
    {
        var t = Sun.OverheadTransits(2026, Qibla.KaabaLatitude, Qibla.KaabaLongitude)[0];
        Assert.True(Sun.Position(t, Qibla.KaabaLatitude, Qibla.KaabaLongitude).Elevation > 89.5);
    }

    [Fact]
    public void Sun_is_down_at_midnight()
    {
        var p = Sun.Position(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero), 51.5, 0);
        Assert.True(p.Elevation < 0);
    }

    [Fact]
    public void Next_shadow_moment_from_london_is_visible_there()
    {
        var now = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero);
        var shadow = QiblaGuide.NextShadow(51.5074, -0.1278, now)!;
        Assert.False(shadow.ShadowPointsToQibla);       // sun over the Kaaba
        Assert.InRange(shadow.At.UtcDateTime.Date, new DateTime(2026, 5, 27), new DateTime(2026, 5, 28));
        Assert.True(Sun.Position(shadow.At, 51.5074, -0.1278).Elevation >= QiblaGuide.MinUsefulSunElevation);
    }

    [Fact]
    public void Montreal_has_no_usable_shadow_moment()
    {
        // All four yearly moments fall at Montreal's sunrise or sunset (elevation ~0°):
        // too low for a shadow, so the guide must say so rather than suggest one.
        var now = new DateTimeOffset(2026, 11, 2, 4, 45, 0, TimeSpan.Zero);
        Assert.Null(QiblaGuide.NextShadow(45.5017, -73.5673, now));
    }

    [Fact]
    public void Reading_gives_turn_from_sun_only_in_daylight()
    {
        var day = QiblaGuide.Read(51.5074, -0.1278, new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero));
        Assert.NotNull(day.TurnFromSun);
        // Sun near due south (≈180°) at noon; Qibla 119° → turn left ~60°.
        Assert.InRange(day.TurnFromSun!.Value, -70, -50);

        var night = QiblaGuide.Read(51.5074, -0.1278, new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero));
        Assert.Null(night.TurnFromSun);
    }
}
