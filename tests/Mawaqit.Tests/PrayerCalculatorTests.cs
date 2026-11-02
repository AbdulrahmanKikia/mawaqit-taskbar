using System.Text.Json;
using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class PrayerCalculatorTests
{
    private sealed record Row(string City, double Lat, double Lon, int Method, int School, string Date, string Timezone, int HijriMonth, string[] Times);
    private sealed record Reference(string Source, string Captured, Row[] Rows);

    private static readonly Reference Aladhan = JsonSerializer.Deserialize<Reference>(
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "aladhan-reference.json")),
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    public static IEnumerable<object[]> Cases() => Aladhan.Rows.Select(r => new object[] { r.City, r.Date });

    [Theory]
    [MemberData(nameof(Cases))]
    public void Matches_aladhan_within_a_minute(string city, string date)
    {
        var row = Aladhan.Rows.Single(r => r.City == city && r.Date == date);
        var method = Enum.GetValues<CalculationMethod>().Single(m => Methods.AladhanId(m) == row.Method);
        var zone = TestData.Zone(row.Timezone);
        var day = DateOnly.Parse(row.Date, System.Globalization.CultureInfo.InvariantCulture);

        var t = PrayerCalculator.Compute(day, new GeoPoint(row.Lat, row.Lon), method,
            row.School == 1 ? AsrSchool.Hanafi : AsrSchool.Standard, HighLatitudeRule.AngleBased);
        var ours = PrayerCalculator.ToDayTimes(t, zone)!;

        string[] names = ["Fajr", "Sunrise", "Dhuhr", "Asr", "Maghrib", "Isha"];
        string[] mine = [ours.Fajr, ours.Shuruq, ours.Dhuhr, ours.Asr, ours.Maghrib, ours.Isha];
        var wrong = new List<string>();
        for (var i = 0; i < 6; i++)
        {
            if (i == 3) continue; // Asr: see Asr_matches_the_exact_shadow_definition
            var diff = Math.Abs((TimeSpan.Parse(mine[i]) - TimeSpan.Parse(row.Times[i])).TotalMinutes);
            diff = Math.Min(diff, 1440 - diff); // 23:59 vs 00:00
            if (diff > 1) wrong.Add($"{names[i]}: ours {mine[i]}, Aladhan {row.Times[i]}");
        }
        Assert.True(wrong.Count == 0, $"{city} {date}: " + string.Join("; ", wrong));
    }

    /// <summary>
    /// Aladhan's Asr drifts from the definition by up to 5.5 min at high latitudes (e.g. Oslo
    /// 2 Nov 2026: exact 13:49, Aladhan 13:54), so Asr is checked against the definition itself:
    /// the moment a stick's shadow equals its noon shadow plus 1 (or 2, Hanafi) stick lengths,
    /// solved with the NOAA sun model from Sun.cs.
    /// </summary>
    [Theory]
    [MemberData(nameof(Cases))]
    public void Asr_matches_the_exact_shadow_definition(string city, string date)
    {
        var row = Aladhan.Rows.Single(r => r.City == city && r.Date == date);
        var method = Enum.GetValues<CalculationMethod>().Single(m => Methods.AladhanId(m) == row.Method);
        var day = DateOnly.Parse(row.Date, System.Globalization.CultureInfo.InvariantCulture);
        var factor = row.School == 1 ? 2 : 1;

        var noon = Sun.SolarNoon(day, row.Lon);
        var decl = Sun.Ephemeris(noon).DeclinationDegrees;
        var target = Math.Atan(1 / (factor + Math.Tan(Math.Abs(row.Lat - decl) * Math.PI / 180))) * 180 / Math.PI;
        var exact = noon;
        while (Sun.Position(exact, row.Lat, row.Lon).Elevation > target) exact = exact.AddSeconds(10);
        var offset = (Methods.Params(method).Offsets ?? [0, 0, 0, 0, 0, 0])[3];

        var ours = PrayerCalculator.Compute(day, new GeoPoint(row.Lat, row.Lon), method,
            row.School == 1 ? AsrSchool.Hanafi : AsrSchool.Standard, HighLatitudeRule.AngleBased).Asr!.Value;

        var diff = Math.Abs((ours - exact.AddMinutes(offset)).TotalMinutes);
        Assert.True(diff <= 1, $"{city} {date} Asr: ours {ours:HH:mm:ss}Z, exact {exact.AddMinutes(offset):HH:mm:ss}Z");
    }

    [Fact]
    public void Fixture_covers_the_hard_cases()
    {
        Assert.Contains(Aladhan.Rows, r => r is { City: "Oslo", Date: "2026-06-21" });     // twilight all night
        Assert.Contains(Aladhan.Rows, r => r is { City: "Makkah", HijriMonth: 9 });        // Ramadan Isha +120
        Assert.Contains(Aladhan.Rows, r => r is { City: "Toronto", Date: "2026-03-08" });  // DST starts
        Assert.Contains(Aladhan.Rows, r => r.School == 1);                                 // Hanafi Asr
    }

    [Fact]
    public void Ramadan_follows_the_um_al_qura_calendar()
    {
        Assert.True(PrayerCalculator.IsRamadan(new DateOnly(2027, 2, 20)));
        Assert.False(PrayerCalculator.IsRamadan(new DateOnly(2026, 11, 2)));
    }

    [Fact]
    public void Polar_night_gives_no_times_instead_of_wrong_ones()
    {
        // Tromsø: the sun does not rise in late December.
        var t = PrayerCalculator.Compute(new DateOnly(2026, 12, 21), new GeoPoint(69.6492, 18.9553),
            CalculationMethod.MuslimWorldLeague, AsrSchool.Standard, HighLatitudeRule.AngleBased);
        Assert.Null(t.Sunrise);
        Assert.Null(PrayerCalculator.ToDayTimes(t, TestData.Zone("Europe/Oslo")));
    }

    [Theory]
    [InlineData(HighLatitudeRule.MiddleOfNight)]
    [InlineData(HighLatitudeRule.OneSeventh)]
    public void High_latitude_rules_keep_fajr_after_isha(HighLatitudeRule rule)
    {
        var t = PrayerCalculator.Compute(new DateOnly(2026, 6, 21), new GeoPoint(59.9139, 10.7522),
            CalculationMethod.MuslimWorldLeague, AsrSchool.Standard, rule);
        Assert.NotNull(t.Fajr);
        Assert.NotNull(t.Isha);
        Assert.True(t.Isha <= t.Fajr!.Value.AddDays(1)); // MiddleOfNight puts both at the midpoint
        Assert.True(t.Fajr < t.Sunrise);
    }

    [Fact]
    public void Times_move_through_the_year()
    {
        var toronto = new CalculatedTimes(new GeoPoint(43.6532, -79.3832), CalculationMethod.Isna, AsrSchool.Standard,
            HighLatitudeRule.AngleBased, TestData.Zone("America/Toronto"));
        var june = toronto.TimesFor(new DateOnly(2026, 6, 21))!;
        var dec = toronto.TimesFor(new DateOnly(2026, 12, 21))!;
        Assert.True(string.CompareOrdinal(june.Maghrib, dec.Maghrib) > 0);
        Assert.True(string.CompareOrdinal(june.Fajr, dec.Fajr) < 0);
    }

    [Theory]
    [InlineData("CA", CalculationMethod.Isna, AsrSchool.Standard)]
    [InlineData("SA", CalculationMethod.UmmAlQura, AsrSchool.Standard)]
    [InlineData("PK", CalculationMethod.Karachi, AsrSchool.Hanafi)]
    [InlineData("FR", CalculationMethod.France, AsrSchool.Standard)]
    [InlineData("GB", CalculationMethod.MuslimWorldLeague, AsrSchool.Standard)]
    [InlineData(null, CalculationMethod.MuslimWorldLeague, AsrSchool.Standard)]
    public void Region_defaults(string? cc, CalculationMethod method, AsrSchool asr)
        => Assert.Equal((method, asr), Methods.DefaultFor(cc));

    [Fact]
    public void Explicit_choice_beats_the_region_default()
    {
        var s = new CalculationSettings { Method = CalculationMethod.Egypt };
        Assert.Equal((CalculationMethod.Egypt, AsrSchool.Hanafi), Methods.Resolve(s, "PK"));
    }
}
