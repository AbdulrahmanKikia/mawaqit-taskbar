using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class ScheduleTests
{
    private static readonly TimeZoneInfo Toronto = TestData.Zone("America/Toronto");
    private static readonly TimeZoneInfo Oslo = TestData.Zone("Europe/Oslo");

    private static DateTimeOffset At(int y, int mo, int d, int h, int mi, TimeZoneInfo zone)
        => TimeZones.ToInstant(new DateTime(y, mo, d, h, mi, 0), zone);

    [Fact]
    public void Tonight_in_Montreal_counts_down_to_tomorrows_fajr_not_a_server_day_ahead()
    {
        var (table, _) = TestData.Parse("montreal-assahaba", "America/Toronto");
        var now = TestData.CapturedAt; // 23:45 on 1 Nov local
        var today = TimeZones.DateIn(now, Toronto);
        Assert.Equal(new DateOnly(2026, 11, 1), today);

        var slots = Days(table, today, Toronto);
        var next = Schedule.Next(slots, now, CountdownTarget.Adhan)!;

        Assert.Equal(PrayerKind.Fajr, next.Kind);
        Assert.Equal(new DateOnly(2026, 11, 2), next.Slot.Date);
        Assert.Equal("04:57", next.Slot.AdhanDisplay);
        Assert.Equal(TimeSpan.FromMinutes(5 * 60 + 12), next.Remaining);
    }

    [Fact]
    public void Isha_after_midnight_belongs_to_the_previous_day_and_is_not_skipped()
    {
        var (table, _) = TestData.Parse("drammen-isha-wrap", "Europe/Oslo");
        var june13 = new DateOnly(2026, 6, 13);
        var now = At(2026, 6, 13, 23, 0, Oslo);

        var next = Schedule.Next(Days(table, june13, Oslo), now, CountdownTarget.Adhan)!;

        Assert.Equal(PrayerKind.Isha, next.Kind);
        Assert.Equal(june13, next.Slot.Date);
        Assert.Equal(At(2026, 6, 14, 0, 0, Oslo), next.At);
        Assert.Equal("00:00", next.Slot.AdhanDisplay);
    }

    [Fact]
    public void Just_after_midnight_yesterdays_late_isha_is_still_next()
    {
        var (table, _) = TestData.Parse("drammen-isha-wrap", "Europe/Oslo");
        // 16 June: Isha 00:02 on the 17th. At 00:01 on the 17th the new day has started.
        var now = At(2026, 6, 17, 0, 1, Oslo);
        var today = TimeZones.DateIn(now, Oslo);

        var next = Schedule.Next(Days(table, today, Oslo), now, CountdownTarget.Adhan)!;

        Assert.Equal(PrayerKind.Isha, next.Kind);
        Assert.Equal(new DateOnly(2026, 6, 16), next.Slot.Date);
    }

    [Fact]
    public void Relative_iqama_is_added_to_adhan()
    {
        var day = new DayTimes("05:00", "06:30", "12:00", "15:00", "17:00", "19:00");
        var iq = new DayIqama("05:20", "+10", "", "+5", "19:15");
        var slots = Schedule.Build(new DateOnly(2026, 3, 1), day, iq, Toronto);

        var dhuhr = slots.Single(s => s.Kind == PrayerKind.Dhuhr);
        Assert.Equal("12:10", dhuhr.IqamaDisplay);
        Assert.Equal(dhuhr.AdhanAt.AddMinutes(10), dhuhr.IqamaAt);
        Assert.Null(slots.Single(s => s.Kind == PrayerKind.Asr).IqamaAt);
        Assert.Null(slots.Single(s => s.Kind == PrayerKind.Shuruq).IqamaDisplay);
    }

    [Fact]
    public void Iqama_past_midnight_is_the_next_day()
    {
        var day = new DayTimes("03:00", "04:30", "13:00", "17:00", "22:40", "23:50");
        var iq = new DayIqama("", "", "", "", "00:10");
        var isha = Schedule.Build(new DateOnly(2026, 6, 20), day, iq, Oslo).Single(s => s.Kind == PrayerKind.Isha);

        Assert.Equal(At(2026, 6, 21, 0, 10, Oslo), isha.IqamaAt);
        Assert.Equal("00:10", isha.IqamaDisplay);
    }

    [Fact]
    public void Nonsense_iqama_is_shown_but_not_counted()
    {
        var day = new DayTimes("05:00", "06:30", "12:00", "15:00", "17:00", "19:00");
        var iq = new DayIqama("", "11:30", "", "", "");
        var dhuhr = Schedule.Build(new DateOnly(2026, 3, 1), day, iq, Toronto).Single(s => s.Kind == PrayerKind.Dhuhr);
        Assert.Null(dhuhr.IqamaAt);
        Assert.Equal("11:30", dhuhr.IqamaDisplay);
    }

    [Fact]
    public void Spring_forward_gap_moves_time_after_the_gap()
    {
        // Toronto skips 02:00–03:00 on 8 March 2026.
        var day = new DayTimes("02:30", "06:30", "12:00", "15:00", "17:00", "19:00");
        var fajr = Schedule.Build(new DateOnly(2026, 3, 8), day, null, Toronto)[0];
        Assert.Equal(new DateTimeOffset(2026, 3, 8, 3, 30, 0, TimeSpan.FromHours(-4)), fajr.AdhanAt);
    }

    [Theory]
    [InlineData(CountdownTarget.Adhan, false, 12, 0)]
    [InlineData(CountdownTarget.Iqama, true, 12, 10)]
    [InlineData(CountdownTarget.AdhanThenIqama, false, 12, 0)]
    public void Countdown_target_picks_adhan_or_iqama(CountdownTarget target, bool toIqama, int h, int m)
    {
        var day = new DayTimes("05:00", "06:30", "12:00", "15:00", "17:00", "19:00");
        var iq = new DayIqama("", "+10", "", "", "");
        var slots = Schedule.Build(new DateOnly(2026, 3, 1), day, iq, Toronto);
        var next = Schedule.Next(slots, At(2026, 3, 1, 11, 0, Toronto), target)!;
        Assert.Equal(PrayerKind.Dhuhr, next.Kind);
        Assert.Equal(toIqama, next.ToIqama);
        Assert.Equal(At(2026, 3, 1, h, m, Toronto), next.At);
    }

    [Fact]
    public void Adhan_then_iqama_moves_on_to_iqama_after_the_call()
    {
        var day = new DayTimes("05:00", "06:30", "12:00", "15:00", "17:00", "19:00");
        var iq = new DayIqama("", "+10", "", "", "");
        var slots = Schedule.Build(new DateOnly(2026, 3, 1), day, iq, Toronto);
        var next = Schedule.Next(slots, At(2026, 3, 1, 12, 5, Toronto), CountdownTarget.AdhanThenIqama)!;
        Assert.True(next.ToIqama);
        Assert.Equal(TimeSpan.FromMinutes(5), next.Remaining);
    }

    [Fact]
    public void Progress_runs_from_the_previous_prayer_to_the_next()
    {
        var day = new DayTimes("05:00", "06:30", "12:00", "15:00", "17:00", "19:00");
        var slots = Schedule.Build(new DateOnly(2026, 3, 1), day, null, Toronto);
        var now = At(2026, 3, 1, 13, 30, Toronto); // halfway between Dhuhr 12:00 and Asr 15:00
        var next = Schedule.Next(slots, now, CountdownTarget.Adhan)!;
        Assert.Equal(0.5, Schedule.Progress(slots, now, next), precision: 3);
    }

    [Theory]
    [InlineData(59, "00:59")]
    [InlineData(3599, "59:59")]
    [InlineData(3600, "1:00:00")]
    [InlineData(-5, "00:00")]
    public void Countdown_format(int seconds, string expected)
        => Assert.Equal(expected, Schedule.FormatCountdown(TimeSpan.FromSeconds(seconds)));

    private static List<PrayerSlot> Days(MosqueTimetable table, DateOnly today, TimeZoneInfo zone)
    {
        var slots = new List<PrayerSlot>();
        foreach (var d in new[] { today.AddDays(-1), today, today.AddDays(1) })
            if (table.TimesFor(d) is { } times)
                slots.AddRange(Schedule.Build(d, times, table.IqamaFor(d), zone));
        return slots;
    }
}
