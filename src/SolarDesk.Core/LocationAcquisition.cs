namespace SolarDesk.Core;

public enum LocationPermission { Unspecified, Allowed, Denied }
public sealed record DeviceLocation(double Latitude, double Longitude, double AccuracyMeters);
public interface IDeviceLocationProvider
{
    Task<LocationPermission> RequestPermissionAsync();
    Task<DeviceLocation> GetLocationAsync();
}

public sealed class LocationPermissionException(string message) : Exception(message);

public static class LocationAcquisition
{
    public static async Task<DeviceLocation> AcquireAsync(IDeviceLocationProvider provider)
    {
        // Never read a location until the OS has returned an explicit Allowed result.
        var permission = await provider.RequestPermissionAsync();
        if (permission == LocationPermission.Denied)
            throw new LocationPermissionException("Location access is off. Open Location permissions, allow location access, then try again.");
        if (permission != LocationPermission.Allowed)
            throw new LocationPermissionException("Windows could not confirm location permission. Open Location permissions, then try again.");
        var location = await provider.GetLocationAsync();
        SolarCalculator.ValidateCoordinates(location.Latitude, location.Longitude);
        return location;
    }
}
