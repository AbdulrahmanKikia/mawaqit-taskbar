using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Mawaqit.Tests.Architecture;

/// <summary>
/// House rules, enforced on every CI run. Each failure says what to do instead.
/// Do not add allowlist entries to make a test pass; fix the code, or get the
/// exception reviewed and explain it next to the entry.
/// </summary>
public class RulesTests
{
    private static readonly string Root = FindRoot();

    // ---- Reviewed allowlists (normally these do not grow) -------------------------

    /// <summary>Files allowed to write files directly. Everything else uses AtomicFile.</summary>
    private static readonly string[] RawFileWriteAllowed =
    [
        "core/Storage/AtomicFile.cs",   // the atomic writer itself
        "host-winui/Data/Updater.cs",    // streams the installer to temp, then verifies its SHA-256
    ];

    // ---- 1. Boundaries ------------------------------------------------------------

    [Fact]
    public void Core_stays_free_of_UI_and_Windows_APIs()
    {
        var banned = new Regex(@"^\s*using\s+(Microsoft\.UI|Microsoft\.Win32|Windows\.|WinRT|System\.Windows)", RegexOptions.Multiline);
        AssertNone(Sources("core", "*.cs"), banned,
            "core/ must stay pure so it runs in tests and CI. Move UI/Windows code to host-winui/ and pass plain data into core.");
    }

    [Fact]
    public void Http_clients_come_from_one_place()
    {
        AssertNone(Sources("host-winui", "*.cs").Concat(Sources("core", "*.cs")), new Regex(@"new\s+HttpClient\s*\("),
            "Create HTTP clients with Http.Create(timeout) (host-winui/Data/Http.cs) so every request identifies the app and has a timeout.",
            allowed: ["host-winui/Data/Http.cs"]);
    }

    // ---- 2. Validate everything that crosses a boundary ---------------------------

    [Fact]
    public void Map_messages_are_parsed_by_the_schema()
    {
        foreach (var (path, text) in Sources("host-winui", "*.cs"))
        {
            if (!Regex.IsMatch(text, @"TryGetWebMessageAsString|WebMessageAsJson")) continue;
            Assert.True(text.Contains("MapIpc.Parse("),
                $"{path} reads WebView messages without MapIpc.Parse. Parse every message with MapIpc.Parse (core/Map/MapIpc.cs) and add new message types there, with tests.");
        }
        AssertNone(Sources("host-winui/Views", "*.cs"), new Regex(@"JsonDocument\.Parse\(|JsonNode\.Parse\("),
            "Views must not hand-parse JSON from the page. Extend MapIpc in core/ instead.");
    }

    [Fact]
    public void Script_calls_into_the_page_go_through_CallJs()
    {
        var hits = Sources("host-winui", "*.cs")
            .SelectMany(f => Regex.Matches(f.Text, @"ExecuteScriptAsync\(").Select(_ => f.Path))
            .ToList();
        Assert.True(hits.Count <= 1 && hits.All(p => p == "host-winui/Views/MapWindow.xaml.cs"),
            $"ExecuteScriptAsync found in: {string.Join(", ", hits)}. Call page functions with CallJs(name, args...) in MapWindow, which JSON-encodes every argument; never build script text from values.");
    }

    // ---- 3. Dangerous patterns ----------------------------------------------------

    [Fact]
    public void Shipped_javascript_has_no_html_injection_or_eval()
    {
        var js = Sources("host-winui/Assets/map", "*.js").Concat(Sources("docs", "*.js"))
            .Where(f => !f.Path.EndsWith("leaflet.js", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(js);
        AssertNone(js, new Regex(@"\beval\s*\(|new\s+Function\s*\(|document\.write|insertAdjacentHTML|\.outerHTML\s*=|setTimeout\(\s*['""`]"),
            "Do not evaluate strings or write raw HTML. Build elements with createElement and set textContent.");
        AssertNone(js, new Regex(@"\.innerHTML\s*=(?!\s*'[^'+`]*'\s*;|\s*""[^""+`]*""\s*;)"),
            "innerHTML may only be set to a fixed string literal. Use textContent / replaceChildren for anything built from data or translations.");
        AssertNone(js, new Regex(@"bind(Tooltip|Popup)\(\s*['""`][^)]*\+"),
            "Pass Leaflet a DOM element (with textContent), not an HTML string built from data.");
    }

    [Fact]
    public void Processes_are_only_started_after_verification()
    {
        AssertNone(Sources("host-winui", "*.cs").Concat(Sources("core", "*.cs")), new Regex(@"Process\.Start\("),
            "Only Updater may start a process, after checking the SHA-256. Open links via Windows.System.Launcher instead.",
            allowed: ["host-winui/Data/Updater.cs"]);
        var updater = Read("host-winui/Data/Updater.cs");
        Assert.True(updater.IndexOf("MatchesSha256", StringComparison.Ordinal) is var check and >= 0
                    && check < updater.IndexOf("Process.Start(", StringComparison.Ordinal),
            "Updater must verify the installer's SHA-256 (ReleaseCheck.MatchesSha256) before Process.Start.");
    }

    [Fact]
    public void Files_are_written_atomically()
    {
        AssertNone(Sources("host-winui", "*.cs").Concat(Sources("core", "*.cs")),
            new Regex(@"(?<![\w.])File\.(WriteAll|AppendAll|Create\(|OpenWrite\()|new\s+FileStream\(|new\s+StreamWriter\("),
            "Write app files with AtomicFile.WriteAllText so a crash cannot leave a half-written config or cache.",
            allowed: RawFileWriteAllowed);
    }

    [Fact]
    public void Errors_are_not_silently_swallowed_in_data_code()
    {
        var empty = new Regex(@"catch\s*(\([^)]*\))?\s*\{\s*(//[^\n]*\s*|/\*.*?\*/\s*)*\}", RegexOptions.Singleline);
        AssertNone(Sources("core", "*.cs").Concat(Sources("host-winui/Data", "*.cs")), empty,
            "Empty catch blocks hide real failures. Log the exception (Serilog) or handle it and say why in a comment inside a real statement.");
    }

    [Fact]
    public void Unsafe_deserialization_is_banned()
        => AssertNone(Sources("host-winui", "*.cs").Concat(Sources("core", "*.cs")),
            new Regex(@"BinaryFormatter|NetDataContractSerializer|TypeNameHandling\.(All|Auto|Objects)|Assembly\.Load(From|File)?\("),
            "Use System.Text.Json or Tomlyn with fixed types; never deserialize types named by the data.");

    // ---- 4. Secrets ----------------------------------------------------------------

    private static readonly (string Name, Regex Pattern)[] SecretPatterns =
    [
        ("Google API key", new Regex(@"AIza[0-9A-Za-z_\-]{35}")),
        ("Neon password", new Regex(@"npg_[A-Za-z0-9]{12,}")),
        ("Postgres URL with password", new Regex(@"postgres(ql)?://[^:\s/]+:[^@\s]{4,}@")),
        ("Private key", new Regex(@"-----BEGIN ([A-Z]+ )?PRIVATE KEY-----")),
        ("Stripe secret key", new Regex(@"\bsk_(live|test)_[0-9A-Za-z]{16,}")),
        ("Resend API key", new Regex(@"\bre_[A-Za-z0-9]{8}_[A-Za-z0-9]{16,}")),
        ("GitHub token", new Regex(@"\b(gh[pousr]_[A-Za-z0-9]{36}|github_pat_[A-Za-z0-9_]{60,})")),
        ("Bearer token value", new Regex(@"BEARER_TOKEN[ \t]*[=:][ \t]*[^\s#""']{8,}")),
    ];

    [Fact]
    public void No_secrets_or_secret_files_in_the_repo()
    {
        var problems = new List<string>();
        foreach (var rel in RepoFiles())
        {
            var name = Path.GetFileName(rel);
            if (name.StartsWith(".env", StringComparison.Ordinal) && name != ".env.example")
                problems.Add($"{rel}: env files hold secrets; keep them untracked (only .env.example is allowed).");
            if (Regex.IsMatch(name, @"\.(pfx|p12|snk|pem|key)$", RegexOptions.IgnoreCase))
                problems.Add($"{rel}: signing keys/certificates must never be committed; use CI secrets.");

            var full = Path.Combine(Root, rel);
            if (IsBinary(rel) || !File.Exists(full) || new FileInfo(full).Length > 2_000_000) continue;
            var text = File.ReadAllText(full);
            foreach (var (label, pattern) in SecretPatterns)
                if (pattern.IsMatch(text))
                    problems.Add($"{rel}: looks like a {label}. Remove it, rotate it, and load it from an environment variable or CI secret.");
        }
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    // ---- 5. Consistency the release depends on -------------------------------------

    [Fact]
    public void Version_is_the_same_everywhere()
    {
        var csproj = Regex.Match(Read("host-winui/Mawaqit.WinUI.csproj"), @"<Version>([^<]+)</Version>").Groups[1].Value;
        var state = Regex.Match(Read("host-winui/Data/AppState.cs"), @"const string Version = ""([^""]+)""").Groups[1].Value;
        var iss = Regex.Match(Read("installer/mawaqit-widget.iss"), @"#define MyAppVersion ""([^""]+)""").Groups[1].Value;
        Assert.True(csproj.Length > 0 && csproj == state && state == iss,
            $"Versions differ (csproj {csproj}, AppState {state}, installer {iss}). Bump all three together.");
        AssertNone(Sources("host-winui", "*.cs"), new Regex(@"MawaqitWidget/\d"),
            "Do not hard-code the version in User-Agent strings; use Http.UserAgent.");
    }

    [Fact]
    public void Toast_app_id_matches_the_installer_shortcut()
    {
        var id = Regex.Match(Read("host-winui/Data/NotificationService.cs"), @"const string AppId = ""([^""]+)""").Groups[1].Value;
        Assert.False(string.IsNullOrEmpty(id));
        Assert.True(Read("installer/mawaqit-widget.iss").Contains($"AppUserModelID: \"{id}\""),
            $"The Start-menu shortcut in installer/mawaqit-widget.iss needs AppUserModelID: \"{id}\" or Windows drops the toasts.");
    }

    [Fact]
    public void Every_string_is_translated_and_every_used_key_exists()
    {
        var source = Read("host-winui/Helpers/LocaleManager.cs");
        var en = Keys(source, "En");
        var fr = Keys(source, "Fr");
        var ar = Keys(source, "Ar");
        var missing = en.Except(fr).Select(k => $"fr: {k}").Concat(en.Except(ar).Select(k => $"ar: {k}")).ToList();
        Assert.True(missing.Count == 0, "Add these translations in LocaleManager.cs: " + string.Join(", ", missing));

        var used = Sources("host-winui", "*.cs")
            .SelectMany(f => Regex.Matches(f.Text, @"\.Get\(""([A-Z][A-Za-z]*\.[A-Za-z0-9.]+)""\)").Select(m => (f.Path, Key: m.Groups[1].Value)))
            .Where(u => !en.Contains(u.Key))
            .Select(u => $"{u.Path}: {u.Key}")
            .Distinct()
            .ToList();
        Assert.True(used.Count == 0, "These keys are used but not defined in En(): " + string.Join(", ", used));
    }

    [Fact]
    public void Releases_only_ship_after_tests_pass()
    {
        var release = Read(".github/workflows/release.yml");
        var test = release.IndexOf("dotnet test", StringComparison.Ordinal);
        var publish = release.IndexOf("build-release.ps1", StringComparison.Ordinal);
        Assert.True(test >= 0 && test < publish,
            "release.yml must run `dotnet test` before it publishes, so a red build can never become a release.");
        Assert.True(File.Exists(Path.Combine(Root, ".github/workflows/ci.yml")),
            "CI must run on every push and pull request (.github/workflows/ci.yml).");
    }

    // ---- helpers --------------------------------------------------------------------

    private static HashSet<string> Keys(string source, string function)
    {
        var start = source.IndexOf($"Dictionary<string, string> {function}() => new()", StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find {function}() in LocaleManager.cs");
        var end = source.IndexOf("\n    };", start, StringComparison.Ordinal);
        return Regex.Matches(source[start..end], @"\[""([^""]+)""\]\s*=").Select(m => m.Groups[1].Value).ToHashSet();
    }

    private static void AssertNone(IEnumerable<(string Path, string Text)> files, Regex pattern, string fix, string[]? allowed = null)
    {
        var hits = new List<string>();
        foreach (var (path, text) in files)
        {
            if (allowed?.Contains(path) == true) continue;
            foreach (Match m in pattern.Matches(text))
            {
                var line = text[..m.Index].Count(c => c == '\n') + 1;
                hits.Add($"  {path}:{line}  {m.Value.Trim()}");
            }
        }
        Assert.True(hits.Count == 0, fix + Environment.NewLine + string.Join(Environment.NewLine, hits));
    }

    private static IEnumerable<(string Path, string Text)> Sources(string dir, string pattern)
    {
        var full = Path.Combine(Root, dir);
        if (!Directory.Exists(full)) yield break;
        foreach (var file in Directory.EnumerateFiles(full, pattern, SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(Root, file).Replace('\\', '/');
            if (rel.Contains("/obj/") || rel.Contains("/bin/")) continue;
            yield return (rel, File.ReadAllText(file));
        }
    }

    private static string Read(string rel) => File.ReadAllText(Path.Combine(Root, rel));

    /// <summary>Tracked plus new-but-not-ignored files, so secrets are caught before the first commit.</summary>
    private static IEnumerable<string> RepoFiles()
    {
        try
        {
            var psi = new ProcessStartInfo("git", "ls-files --cached --others --exclude-standard")
            {
                WorkingDirectory = Root,
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var git = Process.Start(psi)!;
            var output = git.StandardOutput.ReadToEnd();
            git.WaitForExit();
            if (git.ExitCode == 0)
                return output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(p => File.Exists(Path.Combine(Root, p)));
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // git not installed: fall back to walking the tree below.
        }
        return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(Root, f).Replace('\\', '/'))
            .Where(p => !Regex.IsMatch(p, @"(^|/)(\.git|bin|obj|target|dist|node_modules)/"));
    }

    private static bool IsBinary(string rel)
        => Regex.IsMatch(rel, @"\.(ico|png|jpg|jpeg|gif|exe|dll|zip|pdb|ttf|woff2?|mui|pack|idx|rev)$", RegexOptions.IgnoreCase);

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MawaqitWidget.sln"))) return dir.FullName;
        throw new InvalidOperationException("Could not find the repository root (MawaqitWidget.sln).");
    }
}
