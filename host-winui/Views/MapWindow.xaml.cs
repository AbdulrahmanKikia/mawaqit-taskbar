using System.Text.Json;
using Mawaqit.WinUI.Data;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace Mawaqit.WinUI.Views;

public sealed partial class MapWindow : Window
{
    private static MapWindow? _open;

    public MapWindow()
    {
        InitializeComponent();
        Title = "Mawaqit — Find mosque";
        Closed += (_, _) => { if (ReferenceEquals(_open, this)) _open = null; };
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
        web.Source = new Uri("https://mawaqit.local/mosque_map.html");
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
                // map asks for initial nearby via later 'nearby'
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
                await PushMosques(hits, hits.Count == 0 ? "No mosques found" : "", fit: true);
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

    private static double? GetOpt(JsonDocument doc, string name)
        => doc.RootElement.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
