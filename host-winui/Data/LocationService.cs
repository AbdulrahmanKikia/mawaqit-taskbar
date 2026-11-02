using Serilog;
using Windows.Devices.Geolocation;

namespace Mawaqit.WinUI.Data;

public enum LocationAccess { Unknown, Allowed, Denied, Unavailable }

/// <summary>Windows location services (Wi-Fi/GPS). Only used when the user chose times for
/// their location; city-level accuracy is plenty for prayer times.</summary>
internal static class LocationService
{
    public static LocationAccess Access { get; private set; } = LocationAccess.Unknown;

    public static async Task<GeoPoint?> TryGetAsync()
    {
        try
        {
            var status = await Geolocator.RequestAccessAsync();
            Access = status switch
            {
                GeolocationAccessStatus.Allowed => LocationAccess.Allowed,
                GeolocationAccessStatus.Denied => LocationAccess.Denied,
                _ => LocationAccess.Unknown,
            };
            if (Access != LocationAccess.Allowed)
            {
                Log.Information("Windows location access is {Status}", status);
                return null;
            }
            var geo = new Geolocator { DesiredAccuracy = PositionAccuracy.Default };
            var pos = await geo.GetGeopositionAsync(maximumAge: TimeSpan.FromMinutes(30), timeout: TimeSpan.FromSeconds(20));
            var p = pos.Coordinate.Point.Position;
            return new GeoPoint(p.Latitude, p.Longitude);
        }
        catch (Exception ex)
        {
            Access = LocationAccess.Unavailable;
            Log.Warning(ex, "Could not get a location fix");
            return null;
        }
    }

    public static Task OpenWindowsLocationSettingsAsync()
        => Windows.System.Launcher.LaunchUriAsync(new Uri("ms-settings:privacy-location")).AsTask();
}
