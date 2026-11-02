namespace Mawaqit.Core;

/// <summary>Qibla direction by great-circle bearing to the Kaaba. Pure maths: works offline.</summary>
public static class Qibla
{
    public const double KaabaLatitude = 21.422487;
    public const double KaabaLongitude = 39.826206;
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>Degrees clockwise from true north, in [0, 360).</summary>
    public static double Bearing(double latitude, double longitude)
    {
        var p1 = Rad(latitude);
        var p2 = Rad(KaabaLatitude);
        var dl = Rad(KaabaLongitude - longitude);
        var y = Math.Sin(dl) * Math.Cos(p2);
        var x = Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl);
        return Normalize360(Deg(Math.Atan2(y, x)));
    }

    public static double DistanceKm(double latitude, double longitude)
        => GreatCircleKm(latitude, longitude, KaabaLatitude, KaabaLongitude);

    /// <summary>Closer than this, "direction of the Kaaba" stops being meaningful.</summary>
    public static bool IsAtKaaba(double latitude, double longitude) => DistanceKm(latitude, longitude) < 0.1;

    public static double GreatCircleKm(double lat1, double lon1, double lat2, double lon2)
    {
        var dlat = Rad(lat2 - lat1);
        var dlon = Rad(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dlat / 2), 2)
              + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Pow(Math.Sin(dlon / 2), 2);
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>16-point compass index: 0 = N, 1 = NNE, … 15 = NNW.</summary>
    public static int CompassPoint16(double bearing) => (int)Math.Round(Normalize360(bearing) / 22.5) % 16;

    /// <summary>Signed turn from <paramref name="reference"/> to <paramref name="bearing"/>,
    /// in (-180, 180]; positive means turn right (clockwise).</summary>
    public static double TurnFrom(double reference, double bearing)
    {
        var d = Normalize360(bearing - reference);
        return d > 180 ? d - 360 : d;
    }

    public static double Normalize360(double degrees)
    {
        var d = degrees % 360;
        if (d < 0) d += 360;
        return d >= 360 ? 0 : d;
    }

    internal static double Rad(double d) => d * Math.PI / 180;
    internal static double Deg(double r) => r * 180 / Math.PI;
}
