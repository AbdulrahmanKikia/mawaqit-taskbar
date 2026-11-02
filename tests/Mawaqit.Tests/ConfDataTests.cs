using System.Text.Json.Nodes;
using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class ConfDataTests
{
    [Fact]
    public void Montreal_calendar_is_trusted_and_covers_the_whole_template_year()
    {
        var (table, report) = TestData.Parse("montreal-assahaba", "America/Toronto");

        Assert.True(report.CalendarTrusted);
        Assert.Equal(366, report.DaysAccepted);
        Assert.Empty(report.Skipped);
        Assert.Equal("America/Toronto", table.TimeZoneId);
        Assert.Equal(45.5657554, table.Latitude);
        Assert.Equal(-73.5869, table.Longitude);
        Assert.Equal("12:15", table.Jumua);
    }

    [Fact]
    public void Live_times_are_matched_to_the_server_day_not_the_users_day()
    {
        // 23:45 on 1 Nov in Montreal, but Mawaqit's "times" already show 2 Nov.
        var (table, report) = TestData.Parse("montreal-assahaba", "America/Toronto");

        Assert.Equal(new DateOnly(2026, 11, 2), report.LiveTimesMatched);
        var nov1 = table.TimesFor(new DateOnly(2026, 11, 1))!;
        Assert.Equal("04:56", nov1.Fajr);   // what the user must see tonight
        Assert.Equal("18:15", nov1.Isha);
        Assert.Equal("04:57", table.TimesFor(new DateOnly(2026, 11, 2))!.Fajr);
    }

    [Fact]
    public void Iqama_calendar_keeps_relative_entries()
    {
        var (table, _) = TestData.Parse("montreal-assahaba", "America/Toronto");
        var iq = table.IqamaFor(new DateOnly(2026, 11, 1))!;
        Assert.Equal(new DayIqama("05:00", "12:15", "15:30", "+5", "19:15"), iq);
    }

    [Fact]
    public void Leading_imsak_column_is_dropped()
    {
        var (table, report) = TestData.Parse("oslo-imsak-column", "Europe/Oslo");

        Assert.True(report.CalendarTrusted);
        Assert.Equal(366, report.DaysAccepted);
        Assert.Equal(new DayTimes("05:18", "07:34", "12:30", "13:54", "16:27", "18:27"),
            table.TimesFor(new DateOnly(2026, 11, 2)));
    }

    [Fact]
    public void Isha_after_midnight_is_accepted()
    {
        var (table, report) = TestData.Parse("drammen-isha-wrap", "Europe/Oslo");

        Assert.True(report.CalendarTrusted);
        var june13 = table.TimesFor(new DateOnly(2026, 6, 13))!;
        Assert.Equal("22:45", june13.Maghrib);
        Assert.Equal("00:00", june13.Isha);
    }

    [Fact]
    public void Calendar_that_disagrees_with_live_times_falls_back_to_the_published_day()
    {
        var conf = TestData.Conf("montreal-assahaba");
        conf["times"] = new JsonArray("03:00", "11:00", "14:00", "16:00", "18:00");
        conf["shuruq"] = "06:00";

        var (table, report) = ConfData.Parse("x", conf, TestData.CapturedAt, TestData.Zone("America/Toronto"));

        Assert.False(report.CalendarTrusted);
        Assert.Single(table.Days);
        Assert.Contains(report.Skipped, s => s.Contains("does not agree"));
        Assert.Equal("03:00", table.TimesFor(new DateOnly(2026, 11, 1))!.Fajr);
    }

    [Fact]
    public void Broken_rows_are_reported_not_guessed()
    {
        var conf = TestData.Conf("montreal-assahaba");
        var jan = (JsonObject)conf["calendar"]![0]!;
        jan["1"] = new JsonArray("05:00", "07:00", "12:00", "11:00", "16:00", "18:00"); // Asr before Dhuhr
        jan["32"] = new JsonArray("05:00", "07:00", "12:00", "14:00", "16:00", "18:00"); // no such day
        ((JsonObject)conf["iqamaCalendar"]![0]!)["2"] = new JsonArray("05:30", "soon", "", "+5", "19:00");

        var (table, report) = ConfData.Parse("x", conf, TestData.CapturedAt, TestData.Zone("America/Toronto"));

        Assert.True(report.CalendarTrusted);
        Assert.Equal(365, report.DaysAccepted);
        Assert.Null(table.TimesFor(new DateOnly(2026, 1, 1)));
        Assert.Contains(report.Skipped, s => s.StartsWith("calendar 01-1:"));
        Assert.Contains(report.Skipped, s => s.StartsWith("calendar 01-32:"));
        Assert.Contains(report.Skipped, s => s.Contains("'soon'"));
        Assert.Equal("", table.IqamaFor(new DateOnly(2026, 1, 2))!.Dhuhr);
    }

    [Theory]
    [InlineData("montreal-assahaba", "America/Toronto")]
    [InlineData("oslo-imsak-column", "Europe/Oslo")]
    [InlineData("drammen-isha-wrap", "Europe/Oslo")]
    public void Real_calendars_pass_the_sunrise_check(string fixture, string zone)
        => Assert.Empty(TestData.Parse(fixture, zone).Report.Suspicious);

    [Fact]
    public void Sunrise_typo_is_flagged_but_the_day_is_kept()
    {
        // Same kind of typo as Birmingham's real calendar (05:00 for 05:50 on 24 April).
        var conf = TestData.Conf("montreal-assahaba");
        var row = (JsonArray)conf["calendar"]![3]!["24"]!;
        row[1] = "05:00";

        var (table, report) = ConfData.Parse("x", conf, TestData.CapturedAt, TestData.Zone("America/Toronto"));

        Assert.Contains(report.Suspicious, s => s.StartsWith("04-24: sunrise published 05:00"));
        Assert.NotNull(table.TimesFor(new DateOnly(2026, 4, 24)));
    }

    [Fact]
    public void Wrong_mosque_time_zone_is_reported_once()
    {
        var conf = TestData.Conf("oslo-imsak-column");
        conf["timezone"] = "Asia/Tokyo";
        var (_, report) = ConfData.Parse("x", conf, TestData.CapturedAt, TestData.Zone("Europe/Oslo"));
        Assert.Single(report.Suspicious);
        Assert.Contains("looks misconfigured", report.Suspicious[0]);
    }

    [Fact]
    public void Broken_calendar_without_live_times_keeps_only_valid_days_and_says_why()
    {
        var conf = TestData.Conf("montreal-assahaba");
        conf.Remove("times");
        var cal = (JsonArray)conf["calendar"]!;
        for (var m = 0; m < 6; m++)
            foreach (var (day, _) in ((JsonObject)cal[m]!).ToList())
                ((JsonObject)cal[m]!)[day] = new JsonArray("05:00", "04:00", "12:00", "15:00", "17:00", "19:00"); // Fajr after sunrise

        var (table, report) = ConfData.Parse("x", conf, TestData.CapturedAt, TestData.Zone("America/Toronto"));

        Assert.False(report.CalendarTrusted);
        Assert.InRange(report.DaysAccepted, 180, 190);
        Assert.StartsWith("calendar: only", report.Skipped[0]);
        Assert.Contains(report.Skipped, s => s.StartsWith("calendar 01-1:"));
        Assert.Null(table.TimesFor(new DateOnly(2026, 1, 15)));
        Assert.NotNull(table.TimesFor(new DateOnly(2026, 11, 1)));
        Assert.Equal(45.5657554, table.Latitude); // coordinates survive, so times can be calculated
    }

    [Fact]
    public void Missing_days_fall_back_to_calculated_times()
    {
        var (table, _) = TestData.Parse("montreal-assahaba", "America/Toronto");
        table.Days.Remove("01-15");
        var calc = new CalculatedTimes(new GeoPoint(table.Latitude!.Value, table.Longitude!.Value), CalculationMethod.Isna,
            AsrSchool.Standard, HighLatitudeRule.AngleBased, TestData.Zone("America/Toronto"));
        var times = new FallbackTimes(table, calc);

        Assert.Equal(calc.TimesFor(new DateOnly(2026, 1, 15)), times.TimesFor(new DateOnly(2026, 1, 15)));
        Assert.True(times.UsesFallback(new DateOnly(2026, 1, 15)));
        Assert.Null(times.IqamaFor(new DateOnly(2026, 1, 15)));
        Assert.Equal(table.TimesFor(new DateOnly(2026, 11, 1)), times.TimesFor(new DateOnly(2026, 11, 1)));
        Assert.NotNull(times.IqamaFor(new DateOnly(2026, 11, 1)));
    }

    [Fact]
    public void Page_without_any_times_is_rejected()
    {
        var conf = new JsonObject { ["name"] = "Empty" };
        Assert.Throws<TimetableRejectedException>(() =>
            ConfData.Parse("x", conf, TestData.CapturedAt, TimeZoneInfo.Utc));
    }

    [Fact]
    public void Extract_handles_braces_inside_strings()
    {
        var html = """
            <script>
              var confData = {"name":"A } tricky { name","times":["05:00","12:00","15:00","17:00","19:00"],"shuruq":"06:30"};
              var other = {};
            </script>
            """;
        var conf = ConfData.Extract(html);
        Assert.NotNull(conf);
        Assert.Equal("A } tricky { name", (string?)conf!["name"]);
    }

    [Fact]
    public void Extract_returns_null_when_missing_or_broken()
    {
        Assert.Null(ConfData.Extract("<html>nothing here</html>"));
        Assert.Null(ConfData.Extract("var confData = {\"name\": "));
    }

    [Theory]
    [InlineData("5:07", "05:07")]
    [InlineData("05:07", "05:07")]
    [InlineData(" 23:59:30 ", "23:59")]
    public void Times_normalize(string raw, string expected)
    {
        Assert.True(ConfData.TryNormalizeTime(raw, out var t));
        Assert.Equal(expected, t);
    }

    [Theory]
    [InlineData("")]
    [InlineData("24:00")]
    [InlineData("+5")]
    [InlineData("noon")]
    public void Bad_times_are_rejected(string raw) => Assert.False(ConfData.TryNormalizeTime(raw, out _));
}
