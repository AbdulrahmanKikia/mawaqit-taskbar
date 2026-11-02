using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class ConfigTomlTests
{
    [Fact]
    public void Round_trip_keeps_everything()
    {
        var cfg = new AppConfig
        {
            Mosque = new MosqueSelection { MasjidId = "assahaba-mtl", DisplayName = "Mosquée \"Assahaba\" \\ مسجد", Latitude = 45.5657554, Longitude = -73.5869 },
            Countdown = CountdownTarget.AdhanThenIqama,
            ChipSize = ChipSize.Compact,
            Host = HostKind.Strip,
            Language = "ar",
            CustomPosition = 42,
        };
        cfg.Notifications.MinutesBefore = 15;
        cfg.Notifications.AtIqama = true;
        cfg.AddFavorite(new MosqueSelection { MasjidId = "mcq-mtl", DisplayName = "MCQ" });

        var back = ConfigToml.Parse(ConfigToml.Serialize(cfg));

        Assert.Equal(cfg.Mosque.DisplayName, back.Mosque!.DisplayName);
        Assert.Equal(45.5657554, back.Mosque.Latitude);
        Assert.Equal(-73.5869, back.Mosque.Longitude);
        Assert.Equal(CountdownTarget.AdhanThenIqama, back.Countdown);
        Assert.Equal(ChipSize.Compact, back.ChipSize);
        Assert.Equal(HostKind.Strip, back.Host);
        Assert.Equal("ar", back.Language);
        Assert.Equal(42, back.CustomPosition);
        Assert.Equal(15u, back.Notifications.MinutesBefore);
        Assert.True(back.Notifications.AtIqama);
        Assert.Equal("mcq-mtl", Assert.Single(back.Favorites).MasjidId);
        Assert.False(back.Favorites[0].HasLocation);
    }

    [Fact]
    public void Names_with_newlines_and_control_characters_cannot_break_the_file()
    {
        var cfg = new AppConfig { Mosque = new MosqueSelection { MasjidId = "x1", DisplayName = "Line1\nLine2\t\u0001end" } };
        var back = ConfigToml.Parse(ConfigToml.Serialize(cfg));
        Assert.Equal("Line1\nLine2\t\u0001end", back.Mosque!.DisplayName);
    }

    [Fact]
    public void Reads_a_rust_v018_file()
    {
        const string v018 = """
            api_base_url = "http://127.0.0.1:8000"
            start_with_windows = false
            compact = true
            countdown = "adhan-then-iqama"

            [mosque]
            masjid_id = "assahaba-mtl"
            display_name = "Assahaba"

            [notifications]
            minutes_before = 5

            [[favorites]]
            masjid_id = "assahaba-mtl"
            display_name = "Assahaba"

            [[favorites]]
            masjid_id = "assahaba-mtl"
            display_name = "Duplicate"
            """;
        var cfg = ConfigToml.Parse(v018);

        Assert.Equal("", cfg.ApiBaseUrl); // the old localhost default is dropped
        Assert.False(cfg.StartWithWindows);
        Assert.Equal(ChipSize.Compact, cfg.ChipSize);
        Assert.Equal(CountdownTarget.AdhanThenIqama, cfg.Countdown);
        Assert.Equal("assahaba-mtl", cfg.Mosque!.MasjidId);
        Assert.Equal(5u, cfg.Notifications.MinutesBefore);
        Assert.Single(cfg.Favorites);
    }

    [Fact]
    public void Wrong_types_and_out_of_range_values_fall_back()
    {
        const string text = """
            custom_position = "left"
            start_with_windows = "yes"

            [mosque]
            masjid_id = "x1"
            lat = 123.0
            lon = 10

            [notifications]
            minutes_before = -3
            """;
        var cfg = ConfigToml.Parse(text);
        Assert.Equal(-1, cfg.CustomPosition);
        Assert.True(cfg.StartWithWindows);
        Assert.False(cfg.Mosque!.HasLocation);
        Assert.Equal(0u, cfg.Notifications.MinutesBefore);
    }

    [Fact]
    public void Garbage_is_a_corrupt_config_not_a_crash()
        => Assert.Throws<ConfigCorruptException>(() => ConfigToml.Parse("[mosque\nmasjid_id = "));

    [Fact]
    public void Favorites_are_capped_and_deduplicated()
    {
        var cfg = new AppConfig();
        for (var i = 0; i < AppConfig.MaxFavorites; i++)
            Assert.Equal(FavoriteResult.Added, cfg.AddFavorite(new MosqueSelection { MasjidId = $"m{i}" }));
        Assert.Equal(FavoriteResult.AlreadyFavorite, cfg.AddFavorite(new MosqueSelection { MasjidId = "m0" }));
        Assert.Equal(FavoriteResult.Full, cfg.AddFavorite(new MosqueSelection { MasjidId = "m9" }));
        Assert.Equal(FavoriteResult.Invalid, cfg.AddFavorite(new MosqueSelection { MasjidId = " " }));
    }
}
