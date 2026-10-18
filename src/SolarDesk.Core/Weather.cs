using System.Globalization;
using System.Text.Json;

namespace SolarDesk.Core;

public enum WeatherEffect { None, Rain, Snow }

public sealed record WeatherSnapshot(double Celsius, int Code, bool IsDay, DateTimeOffset At)
{
    public bool IsFresh(DateTimeOffset now) => At <= now.AddMinutes(15) && At >= now.AddHours(-2);
    public WeatherEffect Effect => Code switch
    {
        71 or 73 or 75 or 77 or 85 or 86 => WeatherEffect.Snow,
        51 or 53 or 55 or 56 or 57 or 61 or 63 or 65 or 66 or 67 or 80 or 81 or 82 or 95 or 96 or 97 or 99 => WeatherEffect.Rain,
        _ => WeatherEffect.None
    };
    public string Description => Code switch
    {
        0 => IsDay ? "Clear sky" : "Clear night",
        1 => "Mostly clear", 2 => "Partly cloudy", 3 => "Overcast",
        45 or 48 => "Fog", 51 or 53 or 55 => "Drizzle", 56 or 57 => "Freezing drizzle",
        61 => "Light rain", 63 => "Rain", 65 => "Heavy rain", 66 or 67 => "Freezing rain",
        71 => "Light snow", 73 => "Snow", 75 => "Heavy snow", 77 => "Snow grains",
        80 or 81 or 82 => "Rain showers", 85 or 86 => "Snow showers",
        95 or 97 => "Thunderstorm", 96 or 99 => "Storm with hail", _ => "Unknown conditions"
    };

    public static WeatherSnapshot Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("current_units", out var units) || units.ValueKind != JsonValueKind.Object
            || !units.TryGetProperty("temperature_2m", out var unit) || unit.ValueKind != JsonValueKind.String || unit.GetString() != "°C"
            || !root.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object
            || !current.TryGetProperty("temperature_2m", out var temperature) || temperature.ValueKind != JsonValueKind.Number || !temperature.TryGetDouble(out double celsius)
            || !double.IsFinite(celsius) || celsius < -100 || celsius > 65
            || !current.TryGetProperty("weather_code", out var code) || code.ValueKind != JsonValueKind.Number || !code.TryGetInt32(out int number)
            || !current.TryGetProperty("is_day", out var day) || day.ValueKind != JsonValueKind.Number || !day.TryGetInt32(out int dayNumber) || dayNumber is < 0 or > 1
            || !current.TryGetProperty("time", out var time) || time.ValueKind != JsonValueKind.Number || !time.TryGetInt64(out long timestamp)
            || timestamp is < 0 or > 253402300799)
            throw new JsonException("The weather response is incomplete or invalid.");
        return new(celsius, number, dayNumber == 1, DateTimeOffset.FromUnixTimeSeconds(timestamp));
    }
}

public static class WeatherService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(8), MaxResponseContentBufferSize = 65536 };

    // City-scale precision is sufficient for weather; never transmit the saved precise coordinates.
    public static Uri RequestUri(double latitude, double longitude)
    {
        SolarCalculator.ValidateCoordinates(latitude, longitude);
        string lat = Math.Round(latitude, 1).ToString("0.0", CultureInfo.InvariantCulture);
        string lon = Math.Round(longitude, 1).ToString("0.0", CultureInfo.InvariantCulture);
        return new($"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,is_day&temperature_unit=celsius&timeformat=unixtime&timezone=GMT&forecast_days=1");
    }

    public static async Task<WeatherSnapshot> FetchAsync(double latitude, double longitude, CancellationToken cancellation)
        => WeatherSnapshot.Parse(await Client.GetStringAsync(RequestUri(latitude, longitude), cancellation).ConfigureAwait(false));
}
