using System.Globalization;

namespace Mawaqit.Core;

public static class Iqama
{
    /// <summary>One user-typed entry: "13:15", "1:15", "+10", "10" (minutes) → normalized, or "" if unusable.</summary>
    public static string Normalize(string? raw)
    {
        raw = (raw ?? "").Trim();
        if (raw.Length == 0) return "";
        if (ConfData.TryNormalizeTime(raw, out var hhmm)) return hhmm;
        var digits = raw.StartsWith('+') ? raw[1..] : raw;
        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var mins) && mins is > 0 and <= 180
            ? "+" + mins.ToString(CultureInfo.InvariantCulture)
            : "";
    }

    /// <summary>Fajr, Dhuhr, Asr, Maghrib, Isha.</summary>
    public static DayIqama? Parse(IReadOnlyList<string?> five)
    {
        if (five.Count != 5) return null;
        var iq = new DayIqama(Normalize(five[0]), Normalize(five[1]), Normalize(five[2]), Normalize(five[3]), Normalize(five[4]));
        return iq.IsEmpty ? null : iq;
    }
}
