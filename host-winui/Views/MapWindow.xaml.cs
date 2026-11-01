using System.Text.Json;
using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Windows.UI;

namespace Mawaqit.WinUI.Views;

public sealed partial class MapWindow : Window
{
    private static MapWindow? _open;

    internal static XamlRoot? XamlRootForDialog => _open?.Content?.XamlRoot;

    public MapWindow()
    {
        InitializeComponent();
        Title = LocaleManager.Default.Get("Data.AppName");
        WindowChrome.Apply(this, AppTitleBar, 1100, 720);
        ApplyChromeLocale();
        Closed += (_, _) =>
        {
            ThemeHelper.ThemeChanged -= OnThemeChanged;
            LocaleManager.Default.CurrentChanged -= OnLocaleChanged;
            if (ReferenceEquals(_open, this)) _open = null;
        };
        ThemeHelper.ThemeChanged += OnThemeChanged;
        LocaleManager.Default.CurrentChanged += OnLocaleChanged;
        ApplyWebBackground();
        _ = InitAsync();
    }

    public static void Show()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new MapWindow();
        _open.Activate();
    }

    private async Task InitAsync()
    {
        await web.EnsureCoreWebView2Async();
        var folder = Path.Combine(AppContext.BaseDirectory, "Assets", "map");
        web.CoreWebView2.SetVirtualHostNameToFolderMapping(
            "mawaqit.local", folder, CoreWebView2HostResourceAccessKind.Allow);
        web.CoreWebView2.WebMessageReceived += OnMessage;
        web.CoreWebView2.NavigationCompleted += (_, _) =>
        {
            PushTheme();
            PushLang();
        };
        web.Source = new Uri("https://mawaqit.local/mosque_map.html");
    }

    private void OnThemeChanged()
    {
        void Go()
        {
            WindowChrome.ApplyTheme(this);
            ApplyWebBackground();
            PushTheme();
            PushLang();
        }
        if (DispatcherQueue.HasThreadAccess) Go();
        else DispatcherQueue.TryEnqueue(Go);
    }

    private void ApplyWebBackground()
    {
        web.DefaultBackgroundColor = ThemeHelper.GeneralTheme == ElementTheme.Light
            ? Color.FromArgb(255, 243, 243, 243)
            : Color.FromArgb(255, 32, 32, 32);
    }

    private void PushTheme()
    {
        if (web.CoreWebView2 is null) return;
        var (r, g, b, a) = SystemInfos.GetAccentColor();
        if (a == 0 || (r | g | b) == 0) { r = 0; g = 120; b = 212; }
        var accent = $"#{r:X2}{g:X2}{b:X2}";
        var y = (0.2126 * r + 0.7152 * g + 0.0722 * b) / 255.0;
        var accentText = y > 0.55 ? "#1a1a1a" : "#ffffff";
        var dark = ThemeHelper.GeneralTheme == ElementTheme.Dark ? "true" : "false";
        _ = web.ExecuteScriptAsync($"window.__setChrome && window.__setChrome('{accent}', {dark}, '{accentText}')");
    }

    private async void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        var raw = args.TryGetWebMessageAsString();
        if (string.IsNullOrWhiteSpace(raw)) return;
        try
        {
            using var doc = JsonDocument.Parse(raw);
            var type = doc.RootElement.GetProperty("type").GetString();
            var client = AppState.Current.MapClient;
            if (type == "ready")
            {
                PushTheme();
                PushLang();
                return;
            }
            if (type == "nearby")
            {
                var lat = doc.RootElement.GetProperty("lat").GetDouble();
                var lon = doc.RootElement.GetProperty("lon").GetDouble();
                double? N = GetOpt(doc, "north"), S = GetOpt(doc, "south"), E = GetOpt(doc, "east"), W = GetOpt(doc, "west");
                var hits = await client.SearchInViewAsync(lat, lon, N, S, E, W);
                await PushMosques(hits, "", fit: false);
            }
            else if (type == "search")
            {
                var word = doc.RootElement.GetProperty("word").GetString() ?? "";
                var hits = await client.SearchMosquesAsync(word);
                await PushMosques(hits, hits.Count == 0 ? LocaleManager.Default.Get("Map.NoneFound") : "", fit: true);
            }
            else if (type == "select")
            {
                var slug = doc.RootElement.GetProperty("slug").GetString() ?? "";
                var label = doc.RootElement.GetProperty("label").GetString() ?? slug;
                var fav = doc.RootElement.TryGetProperty("favorite", out var f) && f.GetBoolean();
                await AppState.Current.ApplyMosqueAsync(slug, label, fav);
                Close();
            }
        }
        catch (Exception ex)
        {
            var msg = JsonSerializer.Serialize(ex.Message);
            await web.ExecuteScriptAsync($"window.__setError({msg});");
        }
    }

    private async Task PushMosques(List<MosqueHit> hits, string message, bool fit)
    {
        var list = hits.Select(h => new { slug = h.Slug, label = h.Label, lat = h.Lat, lon = h.Lon });
        var payload = JsonSerializer.Serialize(list);
        var msg = JsonSerializer.Serialize(message);
        await web.ExecuteScriptAsync($"window.__setMosques({payload}, {msg}, {(fit ? "true" : "false")});");
    }

    private void OnLocaleChanged(object? sender, EventArgs e)
    {
        void Go()
        {
            ApplyChromeLocale();
            PushLang();
        }
        if (DispatcherQueue.HasThreadAccess) Go();
        else DispatcherQueue.TryEnqueue(Go);
    }

    private void ApplyChromeLocale()
    {
        var loc = LocaleManager.Default;
        Title = loc.Get("Data.AppName");
        titleBarTb.Text = loc.Get("UI.FindMosque");
        if (Content is FrameworkElement root) loc.ApplyFlow(root);
    }

    private void PushLang()
    {
        if (web.CoreWebView2 is null) return;
        var loc = LocaleManager.Default;
        var payload = new Dictionary<string, string>
        {
            ["rtl"] = loc.IsRtl ? "1" : "0",
            ["lang"] = loc.Resolved,
        };
        foreach (var key in LocaleManager.MapKeys)
            payload[key] = loc.Get(key);
        var json = JsonSerializer.Serialize(payload);
        _ = web.ExecuteScriptAsync($"window.__setLang && window.__setLang({json})");
    }

    private static double? GetOpt(JsonDocument doc, string name)
        => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
