namespace SolarDesk.Core;

public sealed record SolarEvent(DateTimeOffset At, bool IsSunrise);
public sealed record SolarDay(DateOnly Date, IReadOnlyList<SolarEvent> Events, bool IsDayAtStart);
public sealed record ScheduleState(double DayAmount, SolarEvent? NextEvent, SolarDay Today, string Phase);

/// <summary>NOAA solar position equations, with numerical horizon crossings in UTC.</summary>
public static class SolarCalculator
{
    private const double Horizon = -0.833;
    private static double Rad(double degrees) => degrees * Math.PI / 180;
    private static double Deg(double radians) => radians * 180 / Math.PI;
    private static double Normalize(double value) => (value % 360 + 360) % 360;

    public static void ValidateCoordinates(double latitude, double longitude)
    {
        if (!double.IsFinite(latitude) || latitude < -90 || latitude > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        if (!double.IsFinite(longitude) || longitude < -180 || longitude > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude));
    }

    public static double Elevation(DateTimeOffset instant, double latitude, double longitude)
    {
        ValidateCoordinates(latitude, longitude);
        var utc = instant.ToUniversalTime();
        double julian = utc.ToUnixTimeMilliseconds() / 86400000.0 + 2440587.5;
        double t = (julian - 2451545.0) / 36525.0;
        double l0 = Normalize(280.46646 + t * (36000.76983 + t * 0.0003032));
        double m = 357.52911 + t * (35999.05029 - 0.0001537 * t);
        double eccentricity = 0.016708634 - t * (0.000042037 + 0.0000001267 * t);
        double center = Math.Sin(Rad(m)) * (1.914602 - t * (0.004817 + 0.000014 * t))
            + Math.Sin(Rad(2 * m)) * (0.019993 - 0.000101 * t) + Math.Sin(Rad(3 * m)) * 0.000289;
        double omega = 125.04 - 1934.136 * t;
        double apparentLongitude = l0 + center - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
        double obliquity = 23 + (26 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60) / 60;
        obliquity += 0.00256 * Math.Cos(Rad(omega));
        double declination = Math.Asin(Math.Sin(Rad(obliquity)) * Math.Sin(Rad(apparentLongitude)));
        double y = Math.Pow(Math.Tan(Rad(obliquity / 2)), 2);
        double equationOfTime = 4 * Deg(y * Math.Sin(2 * Rad(l0)) - 2 * eccentricity * Math.Sin(Rad(m))
            + 4 * eccentricity * y * Math.Sin(Rad(m)) * Math.Cos(2 * Rad(l0))
            - 0.5 * y * y * Math.Sin(4 * Rad(l0)) - 1.25 * eccentricity * eccentricity * Math.Sin(2 * Rad(m)));
        double solarMinutes = (utc.TimeOfDay.TotalMinutes + equationOfTime + 4 * longitude) % 1440;
        if (solarMinutes < 0) solarMinutes += 1440;
        double hourAngle = Rad(solarMinutes / 4 - 180);
        double sinAltitude = Math.Sin(Rad(latitude)) * Math.Sin(declination)
            + Math.Cos(Rad(latitude)) * Math.Cos(declination) * Math.Cos(hourAngle);
        return Deg(Math.Asin(Math.Clamp(sinAltitude, -1, 1)));
    }

    public static SolarDay GetDay(DateOnly date, double latitude, double longitude, TimeZoneInfo zone)
    {
        ValidateCoordinates(latitude, longitude);
        DateTimeOffset start = LocalMidnight(date, zone);
        DateTimeOffset end = LocalMidnight(date.AddDays(1), zone);
        var events = new List<SolarEvent>();
        var previous = start;
        bool previousDay = Elevation(previous, latitude, longitude) >= Horizon;
        bool dayAtStart = previousDay;
        // Scan real elapsed time, so 23/25-hour DST days and date-line locations work naturally.
        while (previous < end)
        {
            var next = previous.AddMinutes(5);
            if (next > end) next = end;
            bool nextDay = Elevation(next, latitude, longitude) >= Horizon;
            if (nextDay != previousDay)
            {
                var left = previous;
                var right = next;
                for (int i = 0; i < 24; i++)
                {
                    var mid = left + (right - left) / 2;
                    if ((Elevation(mid, latitude, longitude) >= Horizon) == previousDay) left = mid;
                    else right = mid;
                }
                if (right < end) events.Add(new SolarEvent(right, nextDay));
            }
            previous = next;
            previousDay = nextDay;
        }
        return new SolarDay(date, events, dayAtStart);
    }

    private static DateTimeOffset LocalMidnight(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // A few zones change clocks at midnight. Start at the first valid local minute.
        while (zone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, zone), TimeSpan.Zero);
    }
}

public sealed class SolarSchedule
{
    private string? cacheKey;
    private List<SolarEvent> cachedEvents = [];
    private SolarDay? today;

    public IReadOnlyList<SolarEvent> GetSolarEvents(DateTimeOffset now, AppSettings settings)
    {
        Evaluate(now, settings);
        return cachedEvents.Select(e => e with
        {
            At = e.At.AddMinutes(-(e.IsSunrise ? settings.SunriseOffsetMinutes : settings.SunsetOffsetMinutes))
        }).OrderBy(e => e.At).ToArray();
    }

    public DateTimeOffset? NextTransitionBoundary(DateTimeOffset now, AppSettings settings)
    {
        Evaluate(now, settings);
        for (int i = 0; i < cachedEvents.Count; i++)
        {
            var half = TransitionHalfMinutes(cachedEvents, i, settings.TransitionMinutes);
            var start = cachedEvents[i].At.AddMinutes(-half);
            var end = cachedEvents[i].At.AddMinutes(half);
            if (start > now) return start;
            // An edit made during a fade remains visible until that fade finishes.
            if (end > now) return end;
        }
        // Polar day/night can have no nearby transition. Check again on later edits;
        // startup always evaluates the current sun position without a saved override.
        return null;
    }

    public ScheduleState Evaluate(DateTimeOffset now, AppSettings settings)
    {
        if (!settings.HasLocation) throw new InvalidOperationException("Choose a location first.");
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
        string key = FormattableString.Invariant($"{localDate:yyyy-MM-dd}|{settings.Latitude}|{settings.Longitude}|{zone.Id}|{settings.SunriseOffsetMinutes}|{settings.SunsetOffsetMinutes}");
        if (key != cacheKey)
        {
            cachedEvents = [];
            for (int day = -2; day <= 2; day++)
            {
                var result = SolarCalculator.GetDay(localDate.AddDays(day), settings.Latitude!.Value, settings.Longitude!.Value, zone);
                if (day == 0) today = result;
                cachedEvents.AddRange(result.Events.Select(e => e with
                {
                    At = e.At.AddMinutes(e.IsSunrise ? settings.SunriseOffsetMinutes : settings.SunsetOffsetMinutes)
                }));
            }
            cachedEvents.Sort((a, b) => a.At.CompareTo(b.At));
            cacheKey = key;
        }
        double amount = CalculateAmount(now, cachedEvents, settings.TransitionMinutes,
            SolarCalculator.Elevation(now, settings.Latitude!.Value, settings.Longitude!.Value) >= -0.833);
        var next = cachedEvents.FirstOrDefault(e => e.At > now);
        string phase = amount <= 0 ? "Night profile" : amount >= 1 ? "Day profile" :
            cachedEvents.OrderBy(e => Math.Abs((e.At - now).TotalMinutes)).First().IsSunrise ? "Fading into day" : "Fading into night";
        return new ScheduleState(amount, next, today!, phase);
    }

    public static double CalculateAmount(DateTimeOffset now, IReadOnlyList<SolarEvent> events, int transitionMinutes, bool fallbackDay)
    {
        if (events.Count == 0) return fallbackDay ? 1 : 0;
        for (int i = 0; i < events.Count; i++)
        {
            var e = events[i];
            double half = TransitionHalfMinutes(events, i, transitionMinutes);
            double delta = (now - e.At).TotalMinutes;
            if (half > 0 && Math.Abs(delta) < half)
            {
                double t = (delta + half) / (2 * half);
                double eased = t * t * (3 - 2 * t);
                return e.IsSunrise ? eased : 1 - eased;
            }
        }
        var previous = events.LastOrDefault(e => e.At <= now);
        return previous is null ? (events[0].IsSunrise ? 0 : 1) : (previous.IsSunrise ? 1 : 0);
    }

    public static double TransitionHalfMinutes(IReadOnlyList<SolarEvent> events, int index, int transitionMinutes)
    {
        double half = Math.Clamp(transitionMinutes, 0, 180) / 2.0;
        if (index > 0) half = Math.Min(half, (events[index].At - events[index - 1].At).TotalMinutes / 2);
        if (index + 1 < events.Count) half = Math.Min(half, (events[index + 1].At - events[index].At).TotalMinutes / 2);
        return half;
    }

    public static int Interpolate(int night, int day, double dayAmount) =>
        (int)Math.Round(Math.Clamp(night + (day - night) * Math.Clamp(dayAmount, 0, 1), 0, 100), MidpointRounding.AwayFromZero);
}
