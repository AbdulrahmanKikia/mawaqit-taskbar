// Usage:
//   dotnet run --project tools/Mawaqit.Probe -- <slug> [<slug>...]
//   dotnet run --project tools/Mawaqit.Probe -- --save-fixture <slug> [name]
// Prints what the widget would accept, skip and show today for each mosque.

using System.Text.Json;
using System.Text.Json.Nodes;
using Mawaqit.Core;

using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "MawaqitWidget-Probe (+https://github.com/AbdulrahmanKikia/mawaqit-taskbar)");

if (args is ["--save-fixture", var fixtureSlug, ..])
{
    var name = args.Length > 2 ? args[2] : fixtureSlug;
    var conf = await Fetch(fixtureSlug);
    string[] keep = ["name", "timezone", "latitude", "longitude", "jumua", "jumua2", "times", "shuruq", "calendar", "iqamaCalendar", "iqama"];
    var trimmed = new JsonObject();
    foreach (var k in keep)
        if (conf[k] is { } v) trimmed[k] = v.DeepClone();
    var path = Path.Combine("tests", "Mawaqit.Tests", "Fixtures", name + ".confdata.json");
    File.WriteAllText(path, trimmed.ToJsonString(new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    Console.WriteLine($"Saved {path} (captured {DateTimeOffset.UtcNow:u})");
    return 0;
}

if (args is ["--compare", var cmpSlug, var methodName, ..])
{
    // Calculated times vs the mosque's own published calendar, every day of the year.
    var method = Enum.Parse<CalculationMethod>(methodName, ignoreCase: true);
    var (table, _) = ConfData.Parse(cmpSlug, await Fetch(cmpSlug), DateTimeOffset.Now, TimeZoneInfo.Local);
    if (table.Latitude is not { } lat || table.Longitude is not { } lon) { Console.WriteLine("no coordinates"); return 1; }
    var zone = TimeZoneInfo.FindSystemTimeZoneById(table.TimeZoneId ?? TimeZoneInfo.Local.Id);
    var calc = new CalculatedTimes(new GeoPoint(lat, lon), method, AsrSchool.Standard, HighLatitudeRule.AngleBased, zone);
    string[] names = ["Fajr", "Shuruq", "Dhuhr", "Asr", "Maghrib", "Isha"];
    var diffs = names.ToDictionary(n => n, _ => new List<(DateOnly Day, int Minutes, string Ours, string Mosque)>());
    for (var d = new DateOnly(DateTime.Now.Year, 1, 1); d.Year == DateTime.Now.Year; d = d.AddDays(1))
    {
        if (table.TimesFor(d) is not { } pub || calc.TimesFor(d) is not { } ours) continue;
        foreach (var n in names)
        {
            var kind = Enum.Parse<PrayerKind>(n);
            var m = (int)(TimeSpan.Parse(ours.For(kind)) - TimeSpan.Parse(pub.For(kind))).TotalMinutes;
            diffs[n].Add((d, m, ours.For(kind), pub.For(kind)));
        }
    }
    Console.WriteLine($"{table.Name}: {method} vs published calendar, {diffs["Fajr"].Count} days (minutes, ours − mosque)");
    foreach (var (n, list) in diffs)
    {
        var mins = list.Select(x => x.Minutes).ToList();
        Console.WriteLine($"   {n,-8} within ±1: {mins.Count(x => Math.Abs(x) <= 1) * 100.0 / mins.Count,5:0.0}%   min {mins.Min(),3}  max {mins.Max(),3}  mean {mins.Average(),5:0.00}");
        foreach (var o in list.Where(x => Math.Abs(x.Minutes) >= 30).Take(3))
            Console.WriteLine($"            outlier {o.Day:MM-dd}: ours {o.Ours}, mosque {o.Mosque}");
    }
    return 0;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Give one or more Mawaqit mosque slugs, e.g. assahaba-mtl");
    return 2;
}

var failures = 0;
foreach (var slug in args)
{
    try
    {
        var conf = await Fetch(slug);
        var now = DateTimeOffset.Now;
        var (table, report) = ConfData.Parse(slug, conf, now, TimeZoneInfo.Local);
        var today = TimeZones.DateIn(now, TimeZoneInfo.Local);
        Console.WriteLine($"== {slug}  ({table.Name}, tz {table.TimeZoneId})");
        Console.WriteLine($"   days {report.DaysAccepted}, iqama days {report.IqamaDaysAccepted}, calendar trusted {report.CalendarTrusted}, live times = {report.LiveTimesMatched}, skipped {report.Skipped.Count}");
        foreach (var s in report.Skipped.Take(5)) Console.WriteLine($"   skip: {s}");
        foreach (var s in report.Suspicious.Take(5)) Console.WriteLine($"   suspicious: {s}");
        if (report.Suspicious.Count > 5) Console.WriteLine($"   … {report.Suspicious.Count - 5} more suspicious");

        var slots = new List<PrayerSlot>();
        foreach (var d in new[] { today.AddDays(-1), today, today.AddDays(1) })
            if (table.TimesFor(d) is { } times)
                slots.AddRange(Schedule.Build(d, times, table.IqamaFor(d), TimeZoneInfo.Local));
        var rows = slots.Where(s => s.Date == today)
            .Select(s => $"{s.Kind} {s.AdhanDisplay}{(s.IqamaDisplay is { } iq ? "/" + iq : "")}");
        Console.WriteLine($"   today {today:yyyy-MM-dd}: {string.Join("  ", rows)}");
        if (Schedule.Next(slots, now, CountdownTarget.Adhan) is { } next)
            Console.WriteLine($"   next: {next.Kind} {next.Slot.Date:MM-dd} {next.Slot.AdhanDisplay} in {Schedule.FormatCountdown(next.Remaining)}");
        if (table.Latitude is { } lat && table.Longitude is { } lon)
        {
            var q = QiblaGuide.Read(lat, lon, now);
            Console.WriteLine($"   qibla {q.Bearing:0.0}° ({q.DistanceKm:N0} km), next shadow: {(q.NextShadow is { } sh ? $"{sh.At.ToLocalTime():yyyy-MM-dd HH:mm} toward={sh.ShadowPointsToQibla}" : "none usable")}");
        }
    }
    catch (Exception ex)
    {
        failures++;
        Console.WriteLine($"== {slug}: FAILED {ex.GetType().Name}: {ex.Message}");
    }
    await Task.Delay(1000); // be polite to mawaqit.net
}
return failures == 0 ? 0 : 1;

async Task<JsonObject> Fetch(string slug)
{
    foreach (var lang in new[] { "en", "fr", "ar" })
    {
        using var resp = await http.GetAsync($"https://mawaqit.net/{lang}/{Uri.EscapeDataString(slug)}");
        if ((int)resp.StatusCode == 404) continue;
        resp.EnsureSuccessStatusCode();
        if (ConfData.Extract(await resp.Content.ReadAsStringAsync()) is { } conf) return conf;
    }
    throw new InvalidOperationException("page not found or confData missing");
}
