using System.Net.Http;

namespace Mawaqit.WinUI.Data;

/// <summary>The one place HttpClients are created, so every request identifies the app
/// (OpenStreetMap/Nominatim policy asks for a contact URL) and has a timeout.</summary>
internal static class Http
{
    public static string UserAgent => $"MawaqitWidget/{AppState.Version} (+https://github.com/{AppState.Repo})";

    public static HttpClient Create(TimeSpan timeout)
    {
        var http = new HttpClient { Timeout = timeout };
        http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        return http;
    }
}
