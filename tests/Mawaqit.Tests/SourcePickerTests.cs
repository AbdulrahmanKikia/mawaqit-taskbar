using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class SourcePickerTests
{
    private static readonly GeoPoint Toronto = new(43.6532, -79.3832);
    private static readonly GeoPoint Brampton = new(43.7315, -79.7624);   // ~32 km from Toronto
    private static readonly GeoPoint Ottawa = new(45.4215, -75.6972);     // ~350 km

    private static AppConfig Config(TimesSource times, GeoPoint? here, MosqueProvider provider = MosqueProvider.Mawaqit)
    {
        var cfg = new AppConfig
        {
            Times = times,
            Mosque = new MosqueSelection { MasjidId = "m", Latitude = Toronto.Latitude, Longitude = Toronto.Longitude, Provider = provider },
        };
        if (here is { } h) { cfg.Location.Latitude = h.Latitude; cfg.Location.Longitude = h.Longitude; }
        return cfg;
    }

    [Fact]
    public void Mosque_mode_uses_the_timetable_wherever_you_are()
        => Assert.Equal(ActiveKind.MosqueTimetable, SourcePicker.Pick(Config(TimesSource.Mosque, Ottawa), true).Kind);

    [Fact]
    public void Mawaqit_mosque_without_a_timetable_yet_is_calculated_for_the_mosque()
    {
        var s = SourcePicker.Pick(Config(TimesSource.Mosque, null), hasMosqueTimetable: false);
        Assert.Equal(ActiveKind.MosqueCalculated, s.Kind);
        Assert.Equal(Toronto, s.Point);
    }

    [Fact]
    public void Osm_mosque_is_calculated_for_its_location()
        => Assert.Equal(ActiveKind.MosqueCalculated, SourcePicker.Pick(Config(TimesSource.Mosque, null, MosqueProvider.OpenStreetMap), true).Kind);

    [Fact]
    public void Location_mode_follows_you()
    {
        var s = SourcePicker.Pick(Config(TimesSource.Location, Ottawa), true);
        Assert.Equal(ActiveKind.LocationCalculated, s.Kind);
        Assert.Equal(Ottawa, s.Point);
    }

    [Fact]
    public void Location_mode_without_a_location_falls_back_to_the_mosque()
        => Assert.Equal(ActiveKind.MosqueTimetable, SourcePicker.Pick(Config(TimesSource.Location, null), true).Kind);

    [Fact]
    public void Automatic_keeps_the_mosque_nearby_and_switches_far_away()
    {
        Assert.Equal(ActiveKind.MosqueTimetable, SourcePicker.Pick(Config(TimesSource.Automatic, Brampton), true).Kind);
        var away = SourcePicker.Pick(Config(TimesSource.Automatic, Ottawa), true);
        Assert.Equal(ActiveKind.LocationCalculated, away.Kind);
        Assert.InRange(away.AwayFromMosqueKm!.Value, 340, 370);
    }

    [Fact]
    public void Nothing_known_means_nothing_shown()
    {
        var cfg = new AppConfig { Times = TimesSource.Automatic };
        Assert.Equal(ActiveKind.None, SourcePicker.Pick(cfg, false).Kind);
    }

    [Theory]
    [InlineData("13:15", "13:15")]
    [InlineData("1:05", "01:05")]
    [InlineData("+10", "+10")]
    [InlineData("10", "+10")]
    [InlineData("soon", "")]
    [InlineData("+500", "")]
    public void Iqama_entries_normalize(string raw, string expected) => Assert.Equal(expected, Iqama.Normalize(raw));
}

public class ConfigLocationTests
{
    [Fact]
    public void Location_calculation_and_osm_mosque_round_trip()
    {
        var cfg = new AppConfig
        {
            Times = TimesSource.Automatic,
            Calculation = new CalculationSettings { Method = CalculationMethod.UmmAlQura, Asr = AsrSchool.Hanafi, HighLatitude = HighLatitudeRule.OneSeventh },
            Mosque = new MosqueSelection
            {
                MasjidId = "osm-n123", DisplayName = "Brampton Islamic Center", Latitude = 43.7019, Longitude = -79.8047,
                Provider = MosqueProvider.OpenStreetMap, Website = "https://example.org/",
                Iqama = new DayIqama("06:00", "+10", "", "+5", "20:00"),
            },
        };
        cfg.Location.Auto = false;
        cfg.Location.Latitude = 43.73;
        cfg.Location.Longitude = -79.76;
        cfg.Location.Name = "Brampton";

        var back = ConfigToml.Parse(ConfigToml.Serialize(cfg));

        Assert.Equal(TimesSource.Automatic, back.Times);
        Assert.Equal(CalculationMethod.UmmAlQura, back.Calculation.Method);
        Assert.Equal(AsrSchool.Hanafi, back.Calculation.Asr);
        Assert.Equal(HighLatitudeRule.OneSeventh, back.Calculation.HighLatitude);
        Assert.False(back.Location.Auto);
        Assert.Equal(new GeoPoint(43.73, -79.76), back.Location.Point);
        Assert.Equal("Brampton", back.Location.Name);
        Assert.Equal(MosqueProvider.OpenStreetMap, back.Mosque!.Provider);
        Assert.Equal("https://example.org/", back.Mosque.Website);
        Assert.Equal(cfg.Mosque.Iqama, back.Mosque.Iqama);
    }

    [Fact]
    public void Auto_method_and_old_files_default_sensibly()
    {
        var cfg = ConfigToml.Parse("language = \"en\"\n");
        Assert.Equal(TimesSource.Mosque, cfg.Times);
        Assert.Null(cfg.Calculation.Method);
        Assert.Null(cfg.Calculation.Asr);
        Assert.True(cfg.Location.Auto);
        Assert.Null(cfg.Location.Point);
        Assert.Contains("method = \"auto\"", ConfigToml.Serialize(cfg));
    }
}

public class MapIpcLocationTests
{
    [Fact]
    public void Location_pick_is_parsed()
    {
        var m = Assert.IsType<MapLocation>(MapIpc.Parse("""{"type":"location","lat":43.7019,"lon":-79.8047,"label":"Brampton"}"""));
        Assert.Equal(new MapLocation(43.7019, -79.8047, "Brampton"), m);
    }

    [Fact]
    public void Osm_mosque_keeps_provider_and_safe_website()
    {
        var m = Assert.IsType<MapSelect>(MapIpc.Parse(
            """{"type":"select","slug":"osm-n123","label":"Brampton Islamic Center","lat":43.7019,"lon":-79.8047,"provider":"osm","website":"https://example.org/"}"""));
        Assert.Equal(MosqueProvider.OpenStreetMap, m.Provider);
        Assert.Equal("https://example.org/", m.Website);

        var js = Assert.IsType<MapSelect>(MapIpc.Parse(
            """{"type":"select","slug":"osm-n1","lat":1,"lon":1,"provider":"osm","website":"javascript:alert(1)"}"""));
        Assert.Null(js.Website);
    }

    [Fact]
    public void Osm_mosque_without_location_is_rejected()
        => Assert.Throws<MapIpcException>(() => MapIpc.Parse("""{"type":"select","slug":"osm-n1","provider":"osm"}"""));
}
