using Windows.Devices.Geolocation;

namespace SolarDesk;

public sealed record DetectedLocation(string Name, double Latitude, double Longitude, string TimeZoneId);

public static class LocationService
{
    public static bool IsWindowsApiAvailable() => Windows.Foundation.Metadata.ApiInformation.IsMethodPresent(
        "Windows.Devices.Geolocation.Geolocator", "RequestAccessAsync");

    public static async Task<DetectedLocation> DetectAsync()
    {
        // Called from the foreground WPF button handler. RequestAccessAsync requires the UI thread.
        var location = await LocationAcquisition.AcquireAsync(new WindowsLocationProvider());
        string name = CityCatalog.Label(location.Latitude, location.Longitude);
        return new DetectedLocation(name, Math.Round(location.Latitude, 4), Math.Round(location.Longitude, 4), TimeZoneInfo.Local.Id);
    }

    private sealed class WindowsLocationProvider : IDeviceLocationProvider
    {
        public async Task<LocationPermission> RequestPermissionAsync()
        {
            var permission = await Geolocator.RequestAccessAsync();
            return permission switch
            {
                GeolocationAccessStatus.Allowed => LocationPermission.Allowed,
                GeolocationAccessStatus.Denied => LocationPermission.Denied,
                _ => LocationPermission.Unspecified
            };
        }

        public async Task<DeviceLocation> GetLocationAsync()
        {
            // City-level accuracy is sufficient. No consentless fallback or IP lookup.
            var geolocator = new Geolocator { DesiredAccuracyInMeters = 1000 };
            var position = await geolocator.GetGeopositionAsync(TimeSpan.FromMinutes(10), TimeSpan.FromSeconds(25));
            var coordinates = position.Coordinate.Point.Position;
            return new DeviceLocation(coordinates.Latitude, coordinates.Longitude, position.Coordinate.Accuracy);
        }
    }
}
