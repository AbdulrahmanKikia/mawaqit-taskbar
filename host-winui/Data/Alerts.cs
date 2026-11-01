namespace Mawaqit.WinUI.Data;

public sealed record Alert(string Key, string Title, string Body);

public static class Alerts
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(90);

    public static IEnumerable<Alert> Pending(
        DateTime date,
        IReadOnlyList<PrayerSlot> slots,
        DateTime now,
        NotificationPrefs prefs,
        HashSet<string> already)
    {
        if (!prefs.Enabled) yield break;
        foreach (var slot in slots)
        {
            if (!prefs.PrayerEnabled(slot.Kind.ToString())) continue;
            var loc = Helpers.LocaleManager.Default;
            var adhanAt = date + slot.Adhan;
            if (prefs.MinutesBefore > 0)
            {
                var before = adhanAt - TimeSpan.FromMinutes(prefs.MinutesBefore);
                if (Due(already, date, slot.Kind, "before", before, now, out var key))
                    yield return new Alert(key,
                        string.Format(loc.Get("Alert.Before"), slot.Label, prefs.MinutesBefore),
                        string.Format(loc.Get("Alert.BeforeBody"), slot.AdhanDisplay));
            }
            if (prefs.AtAdhan && Due(already, date, slot.Kind, "adhan", adhanAt, now, out var k2))
                yield return new Alert(k2,
                    string.Format(loc.Get("Alert.Adhan"), slot.Label),
                    string.Format(loc.Get("Alert.AdhanBody"), slot.AdhanDisplay));
            if (prefs.AtIqama && slot.Iqama is TimeSpan iq)
            {
                var iqAt = date + iq;
                if (Due(already, date, slot.Kind, "iqama", iqAt, now, out var k3))
                    yield return new Alert(k3,
                        string.Format(loc.Get("Alert.Iqama"), slot.Label),
                        string.Format(loc.Get("Alert.IqamaBody"), slot.IqamaDisplay));
            }
        }
    }

    private static bool Due(HashSet<string> already, DateTime date, PrayerKind kind, string kindKey, DateTime at, DateTime now, out string key)
    {
        key = $"{date:yyyy-MM-dd}:{kind}:{kindKey}";
        if (already.Contains(key)) return false;
        return now >= at && now < at + Window;
    }
}
