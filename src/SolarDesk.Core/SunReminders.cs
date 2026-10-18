namespace SolarDesk.Core;

public sealed record SunReminder(string Key, string Title, string Message);

public static class SunReminders
{
    public static SunReminder? FindDue(DateTimeOffset now, AppSettings settings, IReadOnlyList<SolarEvent> solarEvents,
        bool temporarilyOverridden = false, bool hasEligibleDisplays = true)
    {
        if (!settings.SunRemindersEnabled || !settings.HasLocation) return null;
        // A one-minute delivery window covers the ten-second scheduler without replaying stale reminders after sleep.
        var upcoming = solarEvents.FirstOrDefault(e => now >= e.At.AddMinutes(-30) && now < e.At.AddMinutes(-29));
        if (upcoming is null) return null;
        string key = FormattableString.Invariant($"{upcoming.At.ToUnixTimeSeconds()}|{upcoming.IsSunrise}|{settings.Latitude}|{settings.Longitude}");
        if (settings.RecentSunReminders.Contains(key)) return null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        string Clock(DateTimeOffset at)
        {
            var local = TimeZoneInfo.ConvertTime(at, zone);
            return local.ToString(local.Date == localNow.Date ? "HH:mm" : "HH:mm (d MMM)");
        }
        string name = upcoming.IsSunrise ? "Sunrise" : "Sunset";
        string profile = upcoming.IsSunrise ? "Day" : "Night";
        string detail;
        if (!settings.AutoEnabled) detail = "Auto is off; display values will stay as they are.";
        else if (temporarilyOverridden) detail = "Automatic fading is temporarily paused.";
        else if (!hasEligibleDisplays) detail = "No connected displays are enabled for automatic fading.";
        else
        {
            var transitions = solarEvents.Select(e => e with
            {
                At = e.At.AddMinutes(e.IsSunrise ? settings.SunriseOffsetMinutes : settings.SunsetOffsetMinutes)
            }).OrderBy(e => e.At).ToArray();
            var center = upcoming.At.AddMinutes(upcoming.IsSunrise ? settings.SunriseOffsetMinutes : settings.SunsetOffsetMinutes);
            int index = Array.FindIndex(transitions, e => e.At == center && e.IsSunrise == upcoming.IsSunrise);
            double half = SolarSchedule.TransitionHalfMinutes(transitions, index, settings.TransitionMinutes);
            var start = center.AddMinutes(-half);
            var end = center.AddMinutes(half);
            detail = now >= end ? $"The {profile} transition finished at {Clock(end)}."
                : half == 0 ? $"Displays switch to your {profile} profile at {Clock(center)}."
                : now >= start ? $"The {profile} brightness fade is underway until {Clock(end)}."
                : $"{profile} brightness fade: {Clock(start)}–{Clock(end)}.";
        }
        return new SunReminder(key, $"{name} in 30 minutes", $"{name} at {Clock(upcoming.At)} in {settings.LocationName}.\n{detail}");
    }
}
