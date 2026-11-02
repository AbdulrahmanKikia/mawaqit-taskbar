using System.Text.Json;
using Mawaqit.WinUI.Data;
using Mawaqit.WinUI.Helpers;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;
using Serilog;
using Windows.UI;

namespace Mawaqit.WinUI.Views;

public sealed partial class MapWindow : Window
{
    private static MapWindow? _open;
    /// <summary>"mosque" = pick a mosque; "location" = pick a spot as "my location".</summary>
    private string _mode = "mosque";

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

    public static void Show() => Open("mosque");

    public static void ShowForLocation() => Open("location");

    private static void Open(string mode)
    {
        _open ??= new MapWindow();
        _open._mode = mode;
        _open.PushMode();
        _open.Activate();
    }

    private void PushMode()
    {
        var start = AppState.Current.Config.Location.Point ?? AppState.Current.Config.Mosque?.Point;
        CallJs("__setMode", _mode, start?.Latitude, start?.Longitude);
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
        CallJs("__setChrome", accent, ThemeHelper.GeneralTheme == ElementTheme.Dark, accentText);
    }

    private async void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        MapMessage message;
        try
        {
            message = MapIpc.Parse(args.TryGetWebMessageAsString());
        }
        catch (MapIpcException ex)
        {
            Log.Warning("Rejected map message: {Reason}", ex.Message);
            return;
        }

        var seq = message switch { MapNearby n => n.Seq, MapSearch s => s.Seq, _ => 0 };
        try
        {
            var client = AppState.Current.MapClient;
            switch (message)
            {
                case MapReady:
                    PushTheme();
                    PushLang();
                    PushMode();
                    break;
                case MapNearby nearby:
                    // Mawaqit first (fast); OpenStreetMap mosques are added when they arrive.
                    var mawaqit = await client.SearchInViewAsync(nearby.Lat, nearby.Lon, nearby.Bounds);
                    PushMosques(mawaqit, "", fit: false, seq, nearby.Bounds is not null);
                    if (nearby.Bounds is { } view)
                        CallJs("__addMosques", Payload(await client.OsmInViewAsync(view, mawaqit)), seq);
                    break;
                case MapSearch search:
                    var hits = await client.SearchMosquesAsync(search.Word);
                    PushMosques(hits, hits.Count == 0 ? LocaleManager.Default.Get("Map.NoneFound") : "", fit: true, seq);
                    break;
                case MapSelect select:
                    await AppState.Current.ApplyMosqueAsync(select);
                    Close();
                    break;
                case MapLocation spot:
                    await AppState.Current.SetManualLocationAsync(spot);
                    Close();
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Map request failed");
            // Our own InvalidOperationExceptions carry user-facing advice; anything else stays generic.
            var text = ex is InvalidOperationException ? ex.Message : LocaleManager.Default.Get("Map.Error");
            CallJs("__setError", text, seq);
        }
    }

    private void PushMosques(List<MosqueHit> hits, string message, bool fit, int seq, bool moreComing = false)
        => CallJs("__setMosques", Payload(hits), message, fit, seq, moreComing);

    private static IEnumerable<object> Payload(IEnumerable<MosqueHit> hits) => hits.Select(h => new
    {
        slug = h.Slug,
        label = h.Label,
        lat = h.Lat,
        lon = h.Lon,
        provider = h.Provider == MosqueProvider.OpenStreetMap ? "osm" : "mawaqit",
        website = h.Website,
    });

    /// <summary>The only way the host calls into the page: a named window function with
    /// JSON-encoded arguments, so no value is ever spliced into script text.</summary>
    private void CallJs(string function, params object?[] args)
    {
        if (web.CoreWebView2 is null) return;
        var fn = JsonSerializer.Serialize(function);
        var json = JsonSerializer.Serialize(args);
        _ = web.ExecuteScriptAsync($"(function(f,a){{ if (typeof window[f] === 'function') window[f].apply(null, a); }})({fn},{json})");
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
        var loc = LocaleManager.Default;
        var payload = new Dictionary<string, string>
        {
            ["rtl"] = loc.IsRtl ? "1" : "0",
            ["lang"] = loc.Resolved,
        };
        foreach (var key in LocaleManager.MapKeys)
            payload[key] = loc.Get(key);
        CallJs("__setLang", payload);
    }
}
