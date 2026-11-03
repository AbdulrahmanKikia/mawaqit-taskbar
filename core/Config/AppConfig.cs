namespace Mawaqit.Core;

public enum ChipSize
{
    Compact,
    Expanded
}

public enum HostKind
{
    Injected,
    Strip
}

public enum FavoriteResult { Added, AlreadyFavorite, Full, Invalid }

/// <summary>Where prayer times come from.</summary>
public enum TimesSource
{
    /// <summary>The chosen mosque (its Mawaqit timetable, or calculated for its location).</summary>
    Mosque,
    /// <summary>Calculated for where the user is.</summary>
    Location,
    /// <summary>The mosque when nearby; the user's location once they are far from it.</summary>
    Automatic,
}

/// <summary>Mawaqit mosques publish a timetable; OpenStreetMap mosques only give a place.</summary>
public enum MosqueProvider { Mawaqit, OpenStreetMap }

public sealed class LocationPrefs
{
    /// <summary>Follow Windows location services; false = the point the user set on the map.</summary>
    public bool Auto { get; set; } = true;
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string Name { get; set; } = "";

    public GeoPoint? Point => Latitude is { } la && Longitude is { } lo ? new GeoPoint(la, lo) : null;
}

public sealed class MosqueSelection
{
    public string MasjidId { get; set; } = "";
    public string DisplayName { get; set; } = "";
    /// <summary>Mosque coordinates, used offline for the Qibla direction.</summary>
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public MosqueProvider Provider { get; set; } = MosqueProvider.Mawaqit;
    /// <summary>The mosque's own site (from OpenStreetMap), for its official iqama times.</summary>
    public string? Website { get; set; }
    /// <summary>Iqama the user entered for a mosque without a Mawaqit timetable ("HH:mm" or "+N").</summary>
    public DayIqama? Iqama { get; set; }

    public bool HasLocation => Latitude is not null && Longitude is not null;
    public GeoPoint? Point => Latitude is { } la && Longitude is { } lo ? new GeoPoint(la, lo) : null;

    public MosqueSelection Copy() => new()
    {
        MasjidId = MasjidId,
        DisplayName = DisplayName,
        Latitude = Latitude,
        Longitude = Longitude,
        Provider = Provider,
        Website = Website,
        Iqama = Iqama,
    };
}

public sealed class NotificationPrefs
{
    public bool Enabled { get; set; } = true;
    public bool AtAdhan { get; set; } = true;
    public bool AtIqama { get; set; }
    public uint MinutesBefore { get; set; } = 10;
    public bool Fajr { get; set; } = true;
    public bool Dhuhr { get; set; } = true;
    public bool Asr { get; set; } = true;
    public bool Maghrib { get; set; } = true;
    public bool Isha { get; set; } = true;
    public bool Shuruq { get; set; }
    public bool SetupCompleted { get; set; }

    public bool PrayerEnabled(PrayerKind kind) => kind switch
    {
        PrayerKind.Fajr => Fajr,
        PrayerKind.Shuruq => Shuruq,
        PrayerKind.Dhuhr => Dhuhr,
        PrayerKind.Asr => Asr,
        PrayerKind.Maghrib => Maghrib,
        PrayerKind.Isha => Isha,
        _ => false
    };
}

public sealed class AppConfig
{
    public const int MaxFavorites = 5;
    public const int MaxMinutesBefore = 60;

    /// <summary>Kept so the file stays compatible with the Rust v0.1 build; the WinUI host
    /// reads the mosque page directly and no longer calls a local API.</summary>
    public string ApiBaseUrl { get; set; } = "";
    public MosqueSelection? Mosque { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public NotificationPrefs Notifications { get; set; } = new();
    public CountdownTarget Countdown { get; set; } = CountdownTarget.Adhan;
    public List<MosqueSelection> Favorites { get; set; } = new();
    public int CustomPosition { get; set; } = -1;
    public string Display { get; set; } = "PRIMARY";
    public bool AutoAlignment { get; set; } = true;
    public ChipSize ChipSize { get; set; } = ChipSize.Expanded;
    public HostKind Host { get; set; } = HostKind.Injected;
    /// <summary>When true, Chip size appears on the chip right-click menu (still always in Preferences).</summary>
    public bool MenuShowChipSize { get; set; }
    /// <summary>When true, Countdown appears on the chip right-click menu.</summary>
    public bool MenuShowCountdown { get; set; }
    /// <summary>When true, Placement appears on the chip right-click menu.</summary>
    public bool MenuShowPlacement { get; set; }
    public string Language { get; set; } = "system";
    public TimesSource Times { get; set; } = TimesSource.Mosque;
    public CalculationSettings Calculation { get; set; } = new();
    public LocationPrefs Location { get; set; } = new();

    public bool IsFavorite(string masjidId)
        => Favorites.Any(f => f.MasjidId == masjidId);

    public FavoriteResult AddFavorite(MosqueSelection sel)
    {
        if (string.IsNullOrWhiteSpace(sel.MasjidId)) return FavoriteResult.Invalid;
        if (IsFavorite(sel.MasjidId)) return FavoriteResult.AlreadyFavorite;
        if (Favorites.Count >= MaxFavorites) return FavoriteResult.Full;
        Favorites.Add(sel.Copy());
        return FavoriteResult.Added;
    }

    public void RemoveFavorite(string masjidId)
        => Favorites.RemoveAll(f => f.MasjidId == masjidId);
}
