using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mawaqit.Core;

/// <summary>Why a fetched timetable was not trusted.</summary>
public sealed class TimetableRejectedException(string message) : Exception(message);

/// <summary>What the parser accepted, what it skipped, and whether the yearly calendar
/// agreed with the times Mawaqit publishes as "today".</summary>
public sealed record ParseReport(
    int DaysAccepted,
    int IqamaDaysAccepted,
    IReadOnlyList<string> Skipped,
    DateOnly? LiveTimesMatched,
    bool CalendarTrusted)
{
    /// <summary>Kept but doubtful: published sunrise far from the astronomical one (likely a typo).</summary>
    public IReadOnlyList<string> Suspicious { get; init; } = [];
}

/// <summary>
/// Reads the <c>confData</c> JSON embedded in a mosque's mawaqit.net page.
/// Real-data notes (surveyed 2026-11 across FR, GB, NO, SE, MA, AU, CA):
/// <list type="bullet">
/// <item><c>calendar</c>: 12 months of { "day": [fajr, shuruq, dhuhr, asr, maghrib, isha] }.
/// Mosques that display imsak publish a leading 7th column, which we drop.</item>
/// <item><c>times</c> + <c>shuruq</c> are "today" on Mawaqit's server clock, which is a
/// day ahead for the Americas in the evening. Never use them as the user's today;
/// they are only used to check the calendar lines up.</item>
/// <item>Isha can fall after midnight (Nordic summers), so "00:01" after "22:46" is valid.</item>
/// <item><c>iqamaCalendar</c> entries are "HH:mm" or "+N".</item>
/// </list>
/// </summary>
public static class ConfData
{
    private static readonly string[] Markers = ["var confData =", "let confData =", "confData ="];

    /// <summary>Most of a year must survive validation, otherwise the whole calendar is
    /// suspect and we fall back to the single published day.</summary>
    internal const int MinTrustedDays = 330;

    public static JsonObject? Extract(string html)
    {
        foreach (var marker in Markers)
        {
            var at = html.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0) continue;
            var start = html.IndexOf('{', at + marker.Length);
            if (start < 0) continue;
            if (!string.IsNullOrWhiteSpace(html[(at + marker.Length)..start])) continue;

            // Let the JSON reader find the end: braces inside strings (announcements) are fine.
            var bytes = Encoding.UTF8.GetBytes(html[start..]);
            try
            {
                var reader = new Utf8JsonReader(bytes);
                if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) continue;
                reader.Skip();
                var length = checked((int)reader.BytesConsumed);
                if (JsonNode.Parse(bytes.AsSpan(0, length)) is JsonObject obj) return obj;
            }
            catch (JsonException)
            {
                continue;
            }
        }
        return null;
    }

    public static (MosqueTimetable Timetable, ParseReport Report) Parse(
        string slug, JsonObject conf, DateTimeOffset fetchedAt, TimeZoneInfo userZone)
    {
        var skipped = new List<string>();
        var table = new MosqueTimetable
        {
            Slug = slug,
            Name = Str(conf, "name") is { Length: > 0 } n ? n : slug,
            TimeZoneId = Str(conf, "timezone"),
            Latitude = Coordinate(conf, "latitude", 90),
            Longitude = Coordinate(conf, "longitude", 180),
            Jumua = NormalizeOptionalTime(Str(conf, "jumua")),
            Jumua2 = NormalizeOptionalTime(Str(conf, "jumua2")),
            FetchedAt = fetchedAt,
        };

        if (conf["calendar"] is JsonArray calendar)
            ReadCalendar(calendar, table.Days, skipped);
        if (conf["iqamaCalendar"] is JsonArray iqamaCalendar)
            ReadIqamaCalendar(iqamaCalendar, table.Iqama, skipped);
        if (conf["iqama"] is JsonArray fixedRow && ReadIqamaRow(fixedRow, "iqama", skipped) is { IsEmpty: false } fixedIqama)
            table.DefaultIqama = fixedIqama;

        var live = LiveTimes(conf);
        var userToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(fetchedAt, userZone).DateTime);
        DateOnly? matched = live is null ? null : MatchLiveDate(table, live, userToday);
        var trusted = table.Days.Count >= MinTrustedDays && (live is null || matched is not null);

        if (!trusted && live is null)
        {
            // No published "today" to cross-check: keep only the days that validated (the
            // widget calculates the rest) and say what was wrong.
            if (table.Days.Count == 0)
                throw new TimetableRejectedException(
                    "The mosque page has no usable prayer times. " + string.Join("; ", skipped.Take(3)));
            skipped.Insert(0, $"calendar: only {table.Days.Count} days passed validation and there are no live times to check them");
            return (table, new ParseReport(table.Days.Count, table.Iqama.Count, skipped, null, false)
            {
                Suspicious = CheckSunrise(table, fetchedAt, userZone),
            });
        }

        if (!trusted && live is not null)
        {
            if (table.Days.Count > 0)
                skipped.Add(matched is null
                    ? "calendar: does not agree with today's published times; kept only the published day"
                    : $"calendar: only {table.Days.Count} valid days; kept only the published day");

            // Mawaqit's "today" runs on its server clock; when that clock and the user's
            // disagree we cannot tell which date it is, so keep it for the user's today only.
            table.Days.Clear();
            table.Days[MosqueTimetable.Key(userToday)] = live;
            matched = userToday;
        }

        var report = new ParseReport(table.Days.Count, table.Iqama.Count, skipped, matched, trusted)
        {
            Suspicious = CheckSunrise(table, fetchedAt, userZone),
        };
        return (table, report);
    }

    /// <summary>Allowed gap between a published sunrise and the computed one.</summary>
    internal const int SunriseToleranceMinutes = 10;

    /// <summary>
    /// Sunrise depends only on astronomy, not on any calculation method, so it is a fair check
    /// of each published day: a big gap means a typo, or a calendar whose daylight-saving
    /// switch is on another year's dates. Days are kept (we cannot know the right Fajr) and listed.
    /// </summary>
    private static IReadOnlyList<string> CheckSunrise(MosqueTimetable table, DateTimeOffset fetchedAt, TimeZoneInfo userZone)
    {
        if (table.Latitude is not { } lat || table.Longitude is not { } lon || table.Days.Count == 0) return [];
        var zone = userZone;
        if (table.TimeZoneId is { } id && TimeZoneInfo.TryFindSystemTimeZoneById(id, out var mosqueZone)) zone = mosqueZone;

        var year = fetchedAt.UtcDateTime.Year;
        var flagged = new List<string>();
        var checkedDays = 0;
        foreach (var (key, day) in table.Days.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var month = int.Parse(key.AsSpan(0, 2), CultureInfo.InvariantCulture);
            var dom = int.Parse(key.AsSpan(3, 2), CultureInfo.InvariantCulture);
            if (dom > DateTime.DaysInMonth(year, month)) continue; // Feb 29 in a common year
            var date = new DateOnly(year, month, dom);
            var calc = PrayerCalculator.Compute(date, new GeoPoint(lat, lon), CalculationMethod.MuslimWorldLeague,
                AsrSchool.Standard, HighLatitudeRule.AngleBased).Sunrise;
            if (calc is not { } sunrise || !Schedule.TryParseHhmm(day.Shuruq, out var published)) continue;
            checkedDays++;
            var computed = TimeZoneInfo.ConvertTime(sunrise, zone).TimeOfDay;
            var gap = Math.Abs((published - computed).TotalMinutes);
            gap = Math.Min(gap, 1440 - gap);
            if (gap > SunriseToleranceMinutes)
                flagged.Add($"{key}: sunrise published {day.Shuruq}, computed {Schedule.FormatHhmm(computed)} ({gap:0} min apart)");
        }
        // Most days off means the mosque's time zone setting is wrong, not 300 typos.
        if (checkedDays > 0 && flagged.Count > checkedDays / 5)
            return [$"sunrise check: {flagged.Count} of {checkedDays} days disagree; the mosque's time zone ({table.TimeZoneId}) looks misconfigured"];
        return flagged;
    }

    /// <summary>Accepts "H:mm" / "HH:mm" / "HH:mm:ss" and returns "HH:mm".</summary>
    public static bool TryNormalizeTime(string? raw, out string normalized)
    {
        normalized = "";
        if (raw is null) return false;
        raw = raw.Trim();
        foreach (var format in new[] { @"h\:mm", @"hh\:mm", @"hh\:mm\:ss" })
        {
            if (TimeSpan.TryParseExact(raw, format, CultureInfo.InvariantCulture, out var t) && t < TimeSpan.FromDays(1))
            {
                normalized = $"{t.Hours:00}:{t.Minutes:00}";
                return true;
            }
        }
        return false;
    }

    private static void ReadCalendar(JsonArray calendar, Dictionary<string, DayTimes> days, List<string> skipped)
    {
        if (calendar.Count != 12)
        {
            skipped.Add($"calendar: expected 12 months, found {calendar.Count}");
            return;
        }
        for (var m = 0; m < 12; m++)
        {
            if (calendar[m] is not JsonObject month)
            {
                skipped.Add($"calendar month {m + 1}: not an object");
                continue;
            }
            foreach (var (dayKey, value) in month)
            {
                var where = $"calendar {m + 1:00}-{dayKey}";
                if (!TryDate(m + 1, dayKey, out var date)) { skipped.Add($"{where}: not a real date"); continue; }
                if (value is not JsonArray row) { skipped.Add($"{where}: not a list"); continue; }
                if (ReadDay(row) is { } day) days[MosqueTimetable.Key(date)] = day;
                else skipped.Add($"{where}: unexpected times {row.ToJsonString()}");
            }
        }
    }

    private static DayTimes? ReadDay(JsonArray row)
    {
        // Imsak-enabled mosques prepend an imsak column: [imsak, fajr, shuruq, dhuhr, asr, maghrib, isha].
        var offset = row.Count switch { 6 => 0, 7 => 1, _ => -1 };
        if (offset < 0) return null;
        var t = new string[6];
        for (var i = 0; i < 6; i++)
            if (!TryNormalizeTime(Text(row[i + offset]), out t[i])) return null;

        // Fajr < Shuruq < Dhuhr < Asr < Maghrib must hold; Isha may wrap past midnight.
        for (var i = 1; i < 5; i++)
            if (string.CompareOrdinal(t[i - 1], t[i]) >= 0) return null;
        if (string.CompareOrdinal(t[5], t[4]) <= 0 && string.CompareOrdinal(t[5], t[0]) >= 0) return null;

        return new DayTimes(t[0], t[1], t[2], t[3], t[4], t[5]);
    }

    private static void ReadIqamaCalendar(JsonArray calendar, Dictionary<string, DayIqama> iqama, List<string> skipped)
    {
        if (calendar.Count != 12)
        {
            skipped.Add($"iqamaCalendar: expected 12 months, found {calendar.Count}");
            return;
        }
        for (var m = 0; m < 12; m++)
        {
            if (calendar[m] is not JsonObject month) continue;
            foreach (var (dayKey, value) in month)
            {
                var where = $"iqama {m + 1:00}-{dayKey}";
                if (!TryDate(m + 1, dayKey, out var date)) { skipped.Add($"{where}: not a real date"); continue; }
                if (value is JsonArray row && ReadIqamaRow(row, where, skipped) is { IsEmpty: false } day)
                    iqama[MosqueTimetable.Key(date)] = day;
            }
        }
    }

    private static DayIqama? ReadIqamaRow(JsonArray row, string where, List<string> skipped)
    {
        if (row.Count != 5)
        {
            skipped.Add($"{where}: expected 5 iqama entries, found {row.Count}");
            return null;
        }
        var v = new string[5];
        for (var i = 0; i < 5; i++)
        {
            var raw = Text(row[i])?.Trim() ?? "";
            if (raw.Length == 0) v[i] = "";
            else if (TryNormalizeTime(raw, out var hhmm)) v[i] = hhmm;
            else if (raw.StartsWith('+') && int.TryParse(raw.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var mins) && mins <= 180)
                v[i] = "+" + mins.ToString(CultureInfo.InvariantCulture);
            else
            {
                skipped.Add($"{where}: ignored iqama entry '{raw}'");
                v[i] = "";
            }
        }
        return new DayIqama(v[0], v[1], v[2], v[3], v[4]);
    }

    private static DayTimes? LiveTimes(JsonObject conf)
    {
        if (conf["times"] is not JsonArray times || times.Count != 5) return null;
        var row = new JsonArray(
            Text(times[0]), Str(conf, "shuruq"), Text(times[1]), Text(times[2]), Text(times[3]), Text(times[4]));
        return ReadDay(row);
    }

    private static DateOnly? MatchLiveDate(MosqueTimetable table, DayTimes live, DateOnly userToday)
    {
        // The server's day is the user's day, or one either side of it.
        foreach (var candidate in new[] { userToday, userToday.AddDays(1), userToday.AddDays(-1) })
            if (table.TimesFor(candidate) == live) return candidate;
        return null;
    }

    private static bool TryDate(int month, string dayKey, out DateOnly date)
    {
        date = default;
        if (!int.TryParse(dayKey, NumberStyles.None, CultureInfo.InvariantCulture, out var day)) return false;
        // 2024 is a leap year, so Feb 29 from the template is accepted.
        if (day < 1 || day > DateTime.DaysInMonth(2024, month)) return false;
        date = new DateOnly(2024, month, day);
        return true;
    }

    private static string? NormalizeOptionalTime(string? raw)
        => TryNormalizeTime(raw, out var t) ? t : null;

    private static string? Text(JsonNode? node)
        => node is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    private static string? Str(JsonObject conf, string key) => Text(conf[key]);

    private static double? Coordinate(JsonObject conf, string key, double limit)
    {
        if (conf[key] is not JsonValue v) return null;
        double d;
        if (v.TryGetValue<double>(out var dv)) d = dv;
        else if (v.TryGetValue<string>(out var s) && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var sv)) d = sv;
        else return null;
        return double.IsFinite(d) && Math.Abs(d) <= limit ? d : null;
    }
}
