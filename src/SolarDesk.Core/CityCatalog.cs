using System.Globalization;
using System.IO.Compression;

namespace SolarDesk.Core;

public sealed record City(string Name, string AsciiName, string Country, double Latitude, double Longitude, string TimeZoneId, long Population)
{
    public string WindowsTimeZoneId => TimeZoneInfo.TryConvertIanaIdToWindowsId(TimeZoneId, out var id) ? id : TimeZoneId;
}

/// <summary>Offline city labels and search; this lookup makes no network requests.</summary>
public static class CityCatalog
{
    private static readonly Lazy<City[]> Cities = new(Load);

    private static City[] Load()
    {
        using var resource = typeof(CityCatalog).Assembly.GetManifestResourceStream("SolarDesk.Core.Data.cities.tsv.gz")
            ?? throw new InvalidOperationException("The offline city database is missing.");
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var cities = new List<City>();
        while (reader.ReadLine() is { } line)
        {
            var f = line.Split('\t');
            if (f.Length != 7) continue;
            cities.Add(new(f[0], f[1], f[2], double.Parse(f[3], CultureInfo.InvariantCulture),
                double.Parse(f[4], CultureInfo.InvariantCulture), f[5], long.Parse(f[6], CultureInfo.InvariantCulture)));
        }
        return cities.ToArray();
    }

    public static (City City, double DistanceKm) Nearest(double latitude, double longitude)
    {
        SolarCalculator.ValidateCoordinates(latitude, longitude);
        City? nearest = null;
        double distance = double.MaxValue;
        foreach (var city in Cities.Value)
        {
            double candidate = Distance(latitude, longitude, city.Latitude, city.Longitude);
            if (candidate < distance) { nearest = city; distance = candidate; }
        }
        return (nearest ?? throw new InvalidOperationException("The city database is empty."), distance);
    }

    public static string Label(double latitude, double longitude)
    {
        var match = Nearest(latitude, longitude);
        return match.DistanceKm > 150 ? "Saved location" : match.DistanceKm > 20 ? "Near " + match.City.Name : match.City.Name;
    }

    public static IReadOnlyList<City> Search(string query)
    {
        query = query.Trim();
        if (query.Length < 2) return [];
        bool Match(string text) => CultureInfo.InvariantCulture.CompareInfo.IndexOf(text, query,
            CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0;
        return Cities.Value.Where(c => Match(c.Name) || Match(c.AsciiName))
            .OrderByDescending(c => string.Equals(c.Name, query, StringComparison.OrdinalIgnoreCase)
                || string.Equals(c.AsciiName, query, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)
                || c.AsciiName.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(c => c.Population).Take(6).ToArray();
    }

    private static double Distance(double lat1, double lon1, double lat2, double lon2)
    {
        const double rad = Math.PI / 180;
        double a = Math.Pow(Math.Sin((lat2 - lat1) * rad / 2), 2)
            + Math.Cos(lat1 * rad) * Math.Cos(lat2 * rad) * Math.Pow(Math.Sin((lon2 - lon1) * rad / 2), 2);
        return 6371 * 2 * Math.Asin(Math.Sqrt(Math.Clamp(a, 0, 1)));
    }
}
