using System.Net.Http;
using System.Text.Json;
using Serilog;

namespace Mawaqit.WinUI.Data;

public sealed record LatestRelease(string Version, string SetupName, string SetupUrl, string? Sha256, string? Sha256Url);

public sealed class UpdateVerificationException(string message) : Exception(message);

internal static class Updater
{
    public static async Task<LatestRelease> FetchLatestAsync(CancellationToken ct = default)
    {
        using var http = Http.Create(TimeSpan.FromSeconds(30));
        using var resp = await http.GetAsync($"https://api.github.com/repos/{AppState.Repo}/releases/latest", ct);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var version = (doc.RootElement.GetProperty("tag_name").GetString() ?? "").Trim().TrimStart('v');
        string? name = null, url = null, digest = null, sumUrl = null;
        var assets = doc.RootElement.GetProperty("assets").EnumerateArray().ToList();
        foreach (var asset in assets)
        {
            var n = asset.GetProperty("name").GetString() ?? "";
            if (n.StartsWith("MawaqitWidget-Setup-", StringComparison.OrdinalIgnoreCase) && n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                name = n;
                url = asset.GetProperty("browser_download_url").GetString();
                digest = asset.TryGetProperty("digest", out var d) ? ReleaseCheck.ParseSha256(d.GetString()) : null;
                break;
            }
        }
        if (name is null || url is null)
            throw new InvalidOperationException("Latest release has no Setup.exe asset");
        foreach (var asset in assets)
        {
            if (asset.GetProperty("name").GetString() == name + ".sha256")
                sumUrl = asset.GetProperty("browser_download_url").GetString();
        }
        return new LatestRelease(version, name, url, digest, sumUrl);
    }

    public static bool IsNewer(string remote, string local) => ReleaseCheck.IsNewer(remote, local);

    /// <summary>Downloads the installer, checks its SHA-256 against the release, and only
    /// then starts it. A missing or wrong checksum means nothing is run.</summary>
    public static async Task DownloadVerifyAndLaunchAsync(LatestRelease release, CancellationToken ct = default)
    {
        using var http = Http.Create(TimeSpan.FromMinutes(5));
        var expected = release.Sha256;
        if (expected is null && release.Sha256Url is { } sumUrl)
            expected = ReleaseCheck.ParseSha256(await http.GetStringAsync(sumUrl, ct));
        if (expected is null)
            throw new UpdateVerificationException("The release has no checksum, so the installer was not run.");

        var dest = Path.Combine(Path.GetTempPath(), $"{Path.GetFileNameWithoutExtension(release.SetupName)}-{Guid.NewGuid():N}.exe");
        using (var resp = await http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            await using var fs = new FileStream(dest, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            await resp.Content.CopyToAsync(fs, ct);
            fs.Position = 0;
            if (!ReleaseCheck.MatchesSha256(fs, expected))
            {
                fs.Close();
                File.Delete(dest);
                throw new UpdateVerificationException("The downloaded installer did not match the release checksum and was deleted.");
            }
        }
        Log.Information("Update {Version} verified (sha256 {Hash}); starting installer", release.Version, expected);
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dest) { UseShellExecute = true });
    }
}
