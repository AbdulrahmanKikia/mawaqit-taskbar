using System.Text.RegularExpressions;

namespace Mawaqit.Core;

/// <summary>Mawaqit mosque ids ("slugs"), e.g. <c>assahaba-mtl</c>. Real slugs are ASCII
/// letters, digits, '-' and '_' (one French mosque is literally "-37").</summary>
public static partial class MosqueSlug
{
    [GeneratedRegex("^[A-Za-z0-9_-]{1,128}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? slug) => slug is not null && Pattern().IsMatch(slug);

    /// <summary>A pasted mawaqit.net URL or bare slug → slug; anything else → null.</summary>
    public static string? Normalize(string input)
    {
        input = input.Trim();
        if (input.Length == 0) return null;
        if (FromUrl(input) is { } slug) return slug;
        return !input.Contains(' ') && input.Length >= 3 && IsValid(input) ? input.ToLowerInvariant() : null;
    }

    public static string? FromUrl(string input)
    {
        var s = input.Trim().TrimEnd('/');
        var q = s.IndexOfAny(['?', '#']);
        if (q >= 0) s = s[..q].TrimEnd('/');
        const string marker = "mawaqit.net/";
        var idx = s.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;
        var parts = s[(idx + marker.Length)..].Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = parts.Length - 1; i >= 0; i--)
        {
            if (!IsMeta(parts[i].ToLowerInvariant()) && IsValid(parts[i]))
                return parts[i];
        }
        return null;
    }

    private static bool IsMeta(string p) => p is "m" or "mobile" or "www" or "api" or "static"
        or "en" or "fr" or "ar" or "es" or "de" or "nl" or "tr" or "id" or "ur" or "bn" or "ru" or "zh" or "it" or "pt";
}
