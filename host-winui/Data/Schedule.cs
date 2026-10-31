namespace Mawaqit.WinUI.Data;

public enum PrayerKind { Fajr, Shuruq, Dhuhr, Asr, Maghrib, Isha }

public sealed class PrayerSlot
{
    public PrayerKind Kind { get; init; }
    public string Label => Kind.ToString();
    public TimeSpan Adhan { get; init; }
    public string AdhanDisplay { get; init; } = "";
    public TimeSpan? Iqama { get; init; }
    public string? IqamaDisplay { get; init; }
}

public sealed class NextPrayer
{
    public PrayerKind Kind { get; init; }
    public DateTime At { get; init; }
    public TimeSpan Remaining { get; init; }
    public bool ToIqama { get; init; }
}

public static class Schedule
{
    public static IReadOnlyList<PrayerSlot> Build(DateTime date, PrayerTimes times, IqamaTimes iqama)
    {
        var slots = new List<PrayerSlot>();
        foreach (var kind in Enum.GetValues<PrayerKind>())
        {
            var raw = AdhanRaw(kind, times);
            if (!TryParseHhmm(raw, out var adhan)) continue;
            TimeSpan? iq = null;
            string? iqDisp = null;
            var iqRaw = IqamaRaw(kind, iqama);
            if (!string.IsNullOrWhiteSpace(iqRaw))
            {
                iq = ResolveIqama(adhan, iqRaw);
                iqDisp = iq is TimeSpan t ? FormatHhmm(t) : iqRaw;
            }
            slots.Add(new PrayerSlot
            {
                Kind = kind,
                Adhan = adhan,
                AdhanDisplay = FormatHhmm(adhan),
                Iqama = iq,
                IqamaDisplay = iqDisp
            });
        }
        return slots;
    }

    public static NextPrayer? Next(IReadOnlyList<PrayerSlot> slots, DateTime now, CountdownTarget target)
    {
        var today = now.Date;
        foreach (var slot in slots)
        {
            if (slot.Kind == PrayerKind.Shuruq) continue;
            foreach (var (time, toIqama) in Candidates(slot, target))
            {
                var at = today + time;
                if (at > now)
                    return new NextPrayer { Kind = slot.Kind, At = at, Remaining = at - now, ToIqama = toIqama };
            }
        }
        var fajr = slots.FirstOrDefault(s => s.Kind == PrayerKind.Fajr);
        if (fajr is null) return null;
        var (t2, iq2) = Candidates(fajr, target).First();
        var at2 = today.AddDays(1) + t2;
        if (at2 <= now) return null;
        return new NextPrayer { Kind = PrayerKind.Fajr, At = at2, Remaining = at2 - now, ToIqama = iq2 };
    }

    public static string FormatCountdown(TimeSpan remaining)
    {
        var total = Math.Max(0, (int)remaining.TotalSeconds);
        var h = total / 3600;
        var m = (total % 3600) / 60;
        var s = total % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public static string FormatHhmm(TimeSpan t) => $"{(int)t.TotalHours:00}:{t.Minutes:00}";

    private static IEnumerable<(TimeSpan time, bool toIqama)> Candidates(PrayerSlot slot, CountdownTarget target)
    {
        if (target == CountdownTarget.Iqama)
        {
            if (slot.Iqama is TimeSpan iq)
                yield return (iq, true);
            else
                yield return (slot.Adhan, false);
            yield break;
        }

        if (target == CountdownTarget.AdhanThenIqama)
        {
            yield return (slot.Adhan, false);
            if (slot.Iqama is TimeSpan iq2 && iq2 > slot.Adhan)
                yield return (iq2, true);
            yield break;
        }

        yield return (slot.Adhan, false);
    }

    private static string AdhanRaw(PrayerKind kind, PrayerTimes t) => kind switch
    {
        PrayerKind.Fajr => t.Fajr,
        PrayerKind.Shuruq => t.Sunrise,
        PrayerKind.Dhuhr => t.Dohr,
        PrayerKind.Asr => t.Asr,
        PrayerKind.Maghrib => t.Maghreb,
        PrayerKind.Isha => t.Icha,
        _ => ""
    };

    private static string? IqamaRaw(PrayerKind kind, IqamaTimes t) => kind switch
    {
        PrayerKind.Fajr => t.Fajr,
        PrayerKind.Shuruq => null,
        PrayerKind.Dhuhr => t.Dohr,
        PrayerKind.Asr => t.Asr,
        PrayerKind.Maghrib => t.Maghreb,
        PrayerKind.Isha => t.Icha,
        _ => null
    };

    public static bool TryParseHhmm(string raw, out TimeSpan time)
    {
        time = default;
        raw = raw.Trim();
        if (raw.Length == 0) return false;
        if (TimeSpan.TryParseExact(raw, @"h\:mm", null, out time)) return true;
        if (TimeSpan.TryParseExact(raw, @"hh\:mm", null, out time)) return true;
        if (TimeSpan.TryParseExact(raw, @"hh\:mm\:ss", null, out time)) return true;
        return false;
    }

    private static TimeSpan? ResolveIqama(TimeSpan adhan, string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith('+') && int.TryParse(raw[1..], out var mins))
            return adhan + TimeSpan.FromMinutes(mins);
        return TryParseHhmm(raw, out var t) ? t : null;
    }
}
