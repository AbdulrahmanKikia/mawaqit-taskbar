using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Mawaqit.Core;

/// <summary>Version compare + SHA-256 helpers used by the in-app updater and install script.</summary>
public static partial class ReleaseCheck
{
    #region Version compare

    public static bool IsNewer(string remote, string local)
        => Parse(remote).CompareTo(Parse(local)) > 0;

    private static (ulong, ulong, ulong) Parse(string v)
    {
        var parts = v.Trim().TrimStart('v', 'V').Split('.');
        ulong Part(int i)
        {
            if (i >= parts.Length) return 0;
            var digits = new string(parts[i].TakeWhile(char.IsAsciiDigit).ToArray());
            return ulong.TryParse(digits, out var n) ? n : 0;
        }
        return (Part(0), Part(1), Part(2));
    }

    #endregion

    #region Digests

    [GeneratedRegex("^(?:sha256:)?([0-9a-fA-F]{64})$")]
    private static partial Regex DigestPattern();

    /// <summary>"sha256:&lt;hex&gt;" (GitHub asset digest) or a bare hex digest → lowercase hex.</summary>
    public static string? ParseSha256(string? text)
    {
        if (text is null) return null;
        var first = text.Trim().Split((char[])[' ', '\t', '\r', '\n'], 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        var m = first is null ? null : DigestPattern().Match(first);
        return m is { Success: true } ? m.Groups[1].Value.ToLowerInvariant() : null;
    }

    public static bool MatchesSha256(Stream content, string expectedHex)
    {
        var actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        return CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(actual),
            System.Text.Encoding.ASCII.GetBytes(expectedHex.ToLowerInvariant()));
    }

    #endregion

    #region Trusted download URLs

    /// <summary>True when <paramref name="url"/> is a GitHub Releases download for this repo
    /// (declared asset URL from the API; CDN redirects after GET are fine).</summary>
    public static bool IsTrustedReleaseDownloadUrl(string? url, string repo)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(repo)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase)) return false;
        var prefix = $"/{repo.Trim('/')}/releases/download/";
        return uri.AbsolutePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    #endregion
}
