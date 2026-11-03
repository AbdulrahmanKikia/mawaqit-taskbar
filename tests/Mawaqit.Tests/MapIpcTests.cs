using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class MapIpcTests
{
    [Fact]
    public void Nearby_with_bounds()
    {
        var m = Assert.IsType<MapNearby>(MapIpc.Parse(
            """{"type":"nearby","lat":45.5,"lon":-73.5,"north":46,"south":45,"east":-73,"west":-74,"seq":7}"""));
        Assert.Equal(45.5, m.Lat);
        Assert.Equal(new GeoBounds(46, 45, -73, -74), m.Bounds);
        Assert.Equal(7, m.Seq);
    }

    [Fact]
    public void Longitude_past_the_antimeridian_is_wrapped_and_bounds_dropped()
    {
        var m = Assert.IsType<MapNearby>(MapIpc.Parse(
            """{"type":"nearby","lat":10,"lon":286.5,"north":20,"south":0,"east":300,"west":270}"""));
        Assert.Equal(-73.5, m.Lon, precision: 6);
        Assert.Null(m.Bounds);
    }

    [Fact]
    public void Select_carries_coordinates_and_cleans_label()
    {
        var m = Assert.IsType<MapSelect>(MapIpc.Parse(
            """{"type":"select","slug":"assahaba-mtl","label":"Assahaba\u0007 — Montréal","favorite":true,"lat":45.56,"lon":-73.58}"""));
        Assert.Equal("Assahaba — Montréal", m.Label);
        Assert.True(m.Favorite);
        Assert.Equal(45.56, m.Lat);
    }

    [Fact]
    public void Select_without_coordinates_is_fine()
    {
        var m = Assert.IsType<MapSelect>(MapIpc.Parse("""{"type":"select","slug":"-37"}"""));
        Assert.Equal("-37", m.Label);
        Assert.Null(m.Lat);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"type":"drop-tables"}""")]
    [InlineData("""{"type":"nearby","lat":"45","lon":1}""")]
    [InlineData("""{"type":"nearby","lat":95,"lon":1}""")]
    [InlineData("""{"type":"search","word":"   "}""")]
    [InlineData("""{"type":"select","slug":"../config"}""")]
    [InlineData("""{"type":"select","slug":"a b"}""")]
    public void Malformed_messages_are_rejected(string raw)
        => Assert.Throws<MapIpcException>(() => MapIpc.Parse(raw));

    [Fact]
    public void Oversized_search_is_rejected()
        => Assert.Throws<MapIpcException>(() => MapIpc.Parse($$"""{"type":"search","word":"{{new string('a', 201)}}"}"""));
}

public class ReleaseCheckTests
{
    [Theory]
    [InlineData("0.5.0", "0.4.0", true)]
    [InlineData("v0.4.1", "0.4.0", true)]
    [InlineData("0.4.0", "0.4.0", false)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("0.4.0-beta", "0.4.0", false)]
    public void Version_compare(string remote, string local, bool newer)
        => Assert.Equal(newer, ReleaseCheck.IsNewer(remote, local));

    [Fact]
    public void Parses_github_digest_and_sha256sum_lines()
    {
        var hex = new string('a', 64);
        Assert.Equal(hex, ReleaseCheck.ParseSha256("sha256:" + hex.ToUpperInvariant()));
        Assert.Equal(hex, ReleaseCheck.ParseSha256(hex + "  MawaqitWidget-Setup-0.5.0.exe\n"));
        Assert.Null(ReleaseCheck.ParseSha256("md5:abc"));
        Assert.Null(ReleaseCheck.ParseSha256(null));
    }

    [Fact]
    public void Hash_check_detects_tampering()
    {
        var data = "installer bytes"u8.ToArray();
        var good = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data));
        Assert.True(ReleaseCheck.MatchesSha256(new MemoryStream(data), good));
        data[0] ^= 1;
        Assert.False(ReleaseCheck.MatchesSha256(new MemoryStream(data), good));
    }

    [Theory]
    [InlineData("https://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases/download/v0.5.1/MawaqitWidget-Setup-0.5.1.exe", true)]
    [InlineData("https://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases/download/v0.5.1/MawaqitWidget-Setup-0.5.1.exe.sha256", true)]
    [InlineData("https://github.com/evil/mawaqit-taskbar/releases/download/v0.5.1/MawaqitWidget-Setup-0.5.1.exe", false)]
    [InlineData("http://github.com/AbdulrahmanKikia/mawaqit-taskbar/releases/download/v0.5.1/x.exe", false)]
    [InlineData("https://evil.example/MawaqitWidget-Setup-0.5.1.exe", false)]
    [InlineData(null, false)]
    public void Trusted_release_download_urls(string? url, bool ok)
        => Assert.Equal(ok, ReleaseCheck.IsTrustedReleaseDownloadUrl(url, "AbdulrahmanKikia/mawaqit-taskbar"));
}

public class MosqueSlugTests
{
    [Theory]
    [InlineData("https://mawaqit.net/en/assahaba-mtl", "assahaba-mtl")]
    [InlineData("mawaqit.net/fr/m/assahaba-mtl/?x=1#top", "assahaba-mtl")]
    [InlineData("assahaba-mtl", "assahaba-mtl")]
    [InlineData("Assahaba-MTL", "assahaba-mtl")]
    [InlineData("two words", null)]
    [InlineData("https://example.com/assahaba", null)]
    public void Normalize(string input, string? expected) => Assert.Equal(expected, MosqueSlug.Normalize(input));

    [Theory]
    [InlineData("-37", true)]
    [InlineData("quba-islamic-cultural-centre-birmingham-b7-4ny-united-kingdom", true)]
    [InlineData("..", false)]
    [InlineData("a/b", false)]
    [InlineData("", false)]
    public void Valid(string slug, bool ok) => Assert.Equal(ok, MosqueSlug.IsValid(slug));
}

public class RetryBackoffTests
{
    [Fact]
    public void Backs_off_then_caps_and_resets()
    {
        var b = new RetryBackoff();
        var t = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.False(b.IsDue(t));

        var waits = new List<TimeSpan>();
        for (var i = 0; i < 8; i++)
        {
            b.Failed(t);
            waits.Add(b.NextAttempt!.Value - t);
        }
        Assert.Equal(TimeSpan.FromSeconds(30), waits[0]);
        Assert.Equal(TimeSpan.FromMinutes(15), waits[^1]);
        Assert.True(waits.Zip(waits.Skip(1)).All(p => p.First <= p.Second));

        Assert.False(b.IsDue(t.AddMinutes(14)));
        b.RetrySoon(t.AddMinutes(1));
        Assert.True(b.IsDue(t.AddMinutes(1)));

        b.Succeeded();
        Assert.False(b.IsDue(t.AddDays(1)));
    }
}
