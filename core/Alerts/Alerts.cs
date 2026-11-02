namespace Mawaqit.Core;

public enum AlertPhase { Before, Adhan, Iqama }

public sealed record DueAlert(string Key, PrayerSlot Slot, AlertPhase Phase);

public static class Alerts
{
    /// <summary>How late an alert may still fire (e.g. the PC woke from sleep just after adhan).</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    public static IEnumerable<DueAlert> Pending(
        IEnumerable<PrayerSlot> slots, DateTimeOffset now, NotificationPrefs prefs, AlertLedger ledger)
    {
        if (!prefs.Enabled) yield break;
        foreach (var slot in slots)
        {
            if (!prefs.PrayerEnabled(slot.Kind)) continue;
            if (prefs.MinutesBefore > 0)
            {
                var before = slot.AdhanAt - TimeSpan.FromMinutes(prefs.MinutesBefore);
                if (Due(slot, AlertPhase.Before, before, now, ledger) is { } a) yield return a;
            }
            if (prefs.AtAdhan && Due(slot, AlertPhase.Adhan, slot.AdhanAt, now, ledger) is { } b)
                yield return b;
            if (prefs.AtIqama && slot.IqamaAt is { } iq && Due(slot, AlertPhase.Iqama, iq, now, ledger) is { } c)
                yield return c;
        }
    }

    private static DueAlert? Due(PrayerSlot slot, AlertPhase phase, DateTimeOffset at, DateTimeOffset now, AlertLedger ledger)
    {
        var key = $"{slot.Date:yyyy-MM-dd}:{slot.Kind}:{phase}";
        if (ledger.Contains(key)) return null;
        return now >= at && now < at + Window ? new DueAlert(key, slot, phase) : null;
    }
}

/// <summary>Remembers which alerts already fired. Old days are pruned by date, never
/// cleared wholesale, so an alert still inside its window cannot fire twice.</summary>
public sealed class AlertLedger
{
    private readonly Dictionary<string, DateOnly> _fired = new();

    public int Count => _fired.Count;

    public bool Contains(string key) => _fired.ContainsKey(key);

    public void Mark(DueAlert alert) => _fired[alert.Key] = alert.Slot.Date;

    public void PruneBefore(DateOnly date)
    {
        foreach (var key in _fired.Where(kv => kv.Value < date).Select(kv => kv.Key).ToList())
            _fired.Remove(key);
    }
}
