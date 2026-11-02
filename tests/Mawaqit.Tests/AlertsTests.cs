using Mawaqit.Core;
using Xunit;

namespace Mawaqit.Tests;

public class AlertsTests
{
    private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    private static readonly DayTimes Day = new("05:00", "06:30", "12:00", "15:00", "17:00", "19:00");

    [Fact]
    public void Alert_fires_once_inside_its_window()
    {
        var prefs = new NotificationPrefs { MinutesBefore = 0 };
        var ledger = new AlertLedger();
        var slots = Schedule.Build(new DateOnly(2026, 1, 1), Day, null, Utc);
        var dhuhr = new DateTimeOffset(2026, 1, 1, 12, 0, 30, TimeSpan.Zero);

        var first = Alerts.Pending(slots, dhuhr, prefs, ledger).ToList();
        Assert.Single(first);
        Assert.Equal(AlertPhase.Adhan, first[0].Phase);
        ledger.Mark(first[0]);

        Assert.Empty(Alerts.Pending(slots, dhuhr.AddSeconds(1), prefs, ledger));
        Assert.Empty(Alerts.Pending(slots, dhuhr.AddMinutes(2), prefs, new AlertLedger())); // window passed
    }

    [Fact]
    public void A_week_of_alerts_never_fires_twice()
    {
        // The old code cleared its memory after 80 alerts, right after adding the one
        // still inside its window, so that alert fired again.
        var prefs = new NotificationPrefs { AtIqama = true, Shuruq = true, MinutesBefore = 10 };
        var iq = new DayIqama("+10", "+10", "+10", "+10", "+10");
        var ledger = new AlertLedger();
        var fired = new List<string>();
        var start = new DateOnly(2026, 1, 1);

        for (var day = 0; day < 10; day++)
        {
            var date = start.AddDays(day);
            var slots = Schedule.Build(date, Day, iq, Utc);
            var t = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            for (var s = 0; s < 24 * 3600; s += 30) // a tick every 30 s is inside every 90 s window
            {
                var now = t.AddSeconds(s);
                foreach (var alert in Alerts.Pending(slots, now, prefs, ledger))
                {
                    ledger.Mark(alert);
                    fired.Add(alert.Key);
                }
                ledger.PruneBefore(date.AddDays(-1));
            }
        }

        Assert.Equal(fired.Count, fired.Distinct().Count());
        Assert.Equal(10 * (6 * 2 + 5), fired.Count); // 6 prayers × (before + adhan) + 5 iqamas, per day
    }

    [Fact]
    public void Prune_drops_only_older_days()
    {
        var ledger = new AlertLedger();
        var slot = Schedule.Build(new DateOnly(2026, 1, 1), Day, null, Utc)[0];
        ledger.Mark(new DueAlert("old", slot, AlertPhase.Adhan));
        var newer = Schedule.Build(new DateOnly(2026, 1, 3), Day, null, Utc)[0];
        ledger.Mark(new DueAlert("new", newer, AlertPhase.Adhan));

        ledger.PruneBefore(new DateOnly(2026, 1, 2));

        Assert.False(ledger.Contains("old"));
        Assert.True(ledger.Contains("new"));
    }

    [Fact]
    public void Disabled_prayers_and_master_switch_are_respected()
    {
        var slots = Schedule.Build(new DateOnly(2026, 1, 1), Day, null, Utc);
        var dhuhr = new DateTimeOffset(2026, 1, 1, 12, 0, 10, TimeSpan.Zero);
        Assert.Empty(Alerts.Pending(slots, dhuhr, new NotificationPrefs { Dhuhr = false, MinutesBefore = 0 }, new AlertLedger()));
        Assert.Empty(Alerts.Pending(slots, dhuhr, new NotificationPrefs { Enabled = false }, new AlertLedger()));
    }
}
