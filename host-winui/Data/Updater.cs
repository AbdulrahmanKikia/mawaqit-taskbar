using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Mawaqit.WinUI.Data;

public sealed record LatestRelease(string Version, string SetupName, string SetupUrl);

internal static class Updater
{
    public static async Task<LatestRelease> FetchLatestAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"MawaqitWidget/{AppState.Version}");
        var url = $"https://api.github.com/repos/{AppState.Repo}/releases/latest";
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var version = tag.Trim().TrimStart('v');
        string? name = null, setupUrl = null;
        foreach (var asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            var n = asset.GetProperty("name").GetString() ?? "";
            if (n.StartsWith("MawaqitWidget-Setup-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe"))
            {
                name = n;
                setupUrl = asset.GetProperty("browser_download_url").GetString();
                break;
            }
        }
        if (name is null || setupUrl is null)
            throw new InvalidOperationException("Latest release has no Setup.exe asset");
        return new LatestRelease(version, name, setupUrl);
    }

    public static bool IsNewer(string remote, string local)
        => Tuple(remote).CompareTo(Tuple(local)) > 0;

    private static (ulong, ulong, ulong) Tuple(string v)
    {
        var parts = v.Trim().TrimStart('v').Split('.');
        ulong Next(int i)
        {
            if (i >= parts.Length) return 0;
            var digits = new string(parts[i].TakeWhile(char.IsDigit).ToArray());
            return ulong.TryParse(digits, out var n) ? n : 0;
        }
        return (Next(0), Next(1), Next(2));
    }

    public static async Task DownloadAsync(string url, string dest, CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", $"MawaqitWidget/{AppState.Version}");
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        await using var fs = File.Create(dest);
        await resp.Content.CopyToAsync(fs, ct);
    }
}
