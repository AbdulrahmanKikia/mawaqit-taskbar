namespace Mawaqit.Core;

public enum ActiveKind
{
    /// <summary>Nothing to show yet: no mosque and no known location.</summary>
    None,
    /// <summary>The mosque's published Mawaqit timetable.</summary>
    MosqueTimetable,
    /// <summary>Calculated for the mosque's location (mosque not on Mawaqit, or timetable not loaded yet).</summary>
    MosqueCalculated,
    /// <summary>Calculated for where the user is.</summary>
    LocationCalculated,
}

/// <param name="Point">Where calculated times are for (null for a published timetable).</param>
/// <param name="AwayFromMosqueKm">Set when Automatic switched to the user's location because they are far from the mosque.</param>
public sealed record ActiveSource(ActiveKind Kind, GeoPoint? Point, double? AwayFromMosqueKm = null);

public static class SourcePicker
{
    /// <summary>Further than this from the chosen mosque, Automatic uses times for where you are.</summary>
    public const double AwayKm = 50;

    public static ActiveSource Pick(AppConfig cfg, bool hasMosqueTimetable)
    {
        var here = cfg.Location.Point;
        var mosque = MosqueSource(cfg.Mosque, hasMosqueTimetable);

        switch (cfg.Times)
        {
            case TimesSource.Location:
                return here is { } p ? new ActiveSource(ActiveKind.LocationCalculated, p) : mosque;

            case TimesSource.Automatic:
                if (here is { } h && cfg.Mosque?.Point is { } m)
                {
                    var km = Qibla.GreatCircleKm(h.Latitude, h.Longitude, m.Latitude, m.Longitude);
                    if (km > AwayKm) return new ActiveSource(ActiveKind.LocationCalculated, h, km);
                }
                return mosque.Kind != ActiveKind.None ? mosque : Here(here);

            default:
                return mosque.Kind != ActiveKind.None ? mosque : Here(here);
        }
    }

    private static ActiveSource MosqueSource(MosqueSelection? mosque, bool hasTimetable)
    {
        if (mosque is null) return new ActiveSource(ActiveKind.None, null);
        if (mosque.Provider == MosqueProvider.Mawaqit && hasTimetable)
            return new ActiveSource(ActiveKind.MosqueTimetable, mosque.Point);
        return mosque.Point is { } p
            ? new ActiveSource(ActiveKind.MosqueCalculated, p)
            : new ActiveSource(ActiveKind.None, null);
    }

    private static ActiveSource Here(GeoPoint? here)
        => here is { } p ? new ActiveSource(ActiveKind.LocationCalculated, p) : new ActiveSource(ActiveKind.None, null);
}
