using SolarDesk.Core;

int passed = 0, failed = 0;
void Test(string name, Action test)
{
    try { test(); Console.WriteLine("PASS " + name); passed++; }
    catch (Exception e) { Console.WriteLine("FAIL " + name + ": " + e.Message); failed++; }
}
void Check(bool condition, string message = "Assertion failed") { if (!condition) throw new Exception(message); }
void Near(double actual, double expected, double tolerance = 0.001) => Check(Math.Abs(actual - expected) <= tolerance, $"Expected {expected}, got {actual}");
TimeZoneInfo Zone(string id) => TimeZoneInfo.FindSystemTimeZoneById(id);
var warsaw = Zone("Central European Standard Time");
var instant = new DateTimeOffset(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);
var settings = new AppSettings { Latitude = 52.2297, Longitude = 21.0122, TimeZoneId = warsaw.Id };

// Reference: https://www.timeanddate.com/sun/poland/warsaw?month=10&year=2026 (16 October: 07:02 / 17:39).
Test("Warsaw reference sunrise/sunset within 3 minutes", () =>
{
    var day = SolarCalculator.GetDay(new DateOnly(2026, 10, 16), 52.2297, 21.0122, warsaw);
    Check(day.Events.Count == 2);
    Near(TimeZoneInfo.ConvertTime(day.Events[0].At, warsaw).TimeOfDay.TotalMinutes, 7 * 60 + 2, 3);
    Near(TimeZoneInfo.ConvertTime(day.Events[1].At, warsaw).TimeOfDay.TotalMinutes, 17 * 60 + 39, 3);
});
Test("New York summer sunrise/sunset within 3 minutes", () =>
{
    var zone = Zone("Eastern Standard Time");
    var day = SolarCalculator.GetDay(new DateOnly(2026, 6, 21), 40.7128, -74.006, zone);
    Near(TimeZoneInfo.ConvertTime(day.Events.Single(e => e.IsSunrise).At, zone).TimeOfDay.TotalMinutes, 5 * 60 + 25, 3);
    Near(TimeZoneInfo.ConvertTime(day.Events.Single(e => !e.IsSunrise).At, zone).TimeOfDay.TotalMinutes, 20 * 60 + 30, 3);
});
Test("Sydney southern summer sunrise/sunset within 3 minutes", () =>
{
    var zone = Zone("AUS Eastern Standard Time");
    var day = SolarCalculator.GetDay(new DateOnly(2026, 12, 21), -33.8688, 151.2093, zone);
    Near(TimeZoneInfo.ConvertTime(day.Events.Single(e => e.IsSunrise).At, zone).TimeOfDay.TotalMinutes, 5 * 60 + 41, 3);
    Near(TimeZoneInfo.ConvertTime(day.Events.Single(e => !e.IsSunrise).At, zone).TimeOfDay.TotalMinutes, 20 * 60 + 5, 3);
});
Test("DST spring-forward uses the location offset for each event", () =>
{
    var before = SolarCalculator.GetDay(new DateOnly(2026, 3, 28), 52.2297, 21.0122, warsaw);
    var after = SolarCalculator.GetDay(new DateOnly(2026, 3, 29), 52.2297, 21.0122, warsaw);
    var a = TimeZoneInfo.ConvertTime(before.Events[0].At, warsaw);
    var b = TimeZoneInfo.ConvertTime(after.Events[0].At, warsaw);
    Near(a.Offset.TotalHours, 1); Near(b.Offset.TotalHours, 2);
    Check((b.TimeOfDay - a.TimeOfDay).TotalMinutes is > 55 and < 60);
});
Test("DST fall-back has exactly one sunrise and sunset", () =>
{
    var day = SolarCalculator.GetDay(new DateOnly(2026, 10, 25), 52.2297, 21.0122, warsaw);
    Check(day.Events.Count == 2); Near(TimeZoneInfo.ConvertTime(day.Events[0].At, warsaw).Offset.TotalHours, 1);
});
Test("Leap day", () => Check(SolarCalculator.GetDay(new DateOnly(2028, 2, 29), 52.2297, 21.0122, warsaw).Events.Count == 2));
Test("Polar summer remains day without horizon crossings", () =>
{
    var polar = new AppSettings { Latitude = 69.6492, Longitude = 18.9553, TimeZoneId = "W. Europe Standard Time" };
    var state = new SolarSchedule().Evaluate(new DateTimeOffset(2026, 6, 21, 0, 0, 0, TimeSpan.Zero), polar);
    Check(state.Today.Events.Count == 0); Near(state.DayAmount, 1);
});
Test("Polar winter remains night without horizon crossings", () =>
{
    var polar = new AppSettings { Latitude = 69.6492, Longitude = 18.9553, TimeZoneId = "W. Europe Standard Time" };
    var state = new SolarSchedule().Evaluate(new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.Zero), polar);
    Check(state.Today.Events.Count == 0); Near(state.DayAmount, 0);
});
Test("International date line uses local date", () =>
{
    var zone = Zone("Line Islands Standard Time");
    var date = new DateOnly(2026, 10, 16);
    var day = SolarCalculator.GetDay(date, 1.8721, -157.4278, zone);
    Check(day.Events.Count == 2);
    foreach (var e in day.Events) Check(DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(e.At, zone).DateTime) == date);
    Check(day.Events[0].At.UtcDateTime.Date == new DateTime(2026, 10, 15));
});
var rise = new SolarEvent(instant, true);
var set = new SolarEvent(instant.AddHours(10), false);
var events = new[] { rise, set };
Test("30-minute sunrise fade endpoints and center", () =>
{
    Near(SolarSchedule.CalculateAmount(instant.AddMinutes(-15), events, 30, false), 0);
    Near(SolarSchedule.CalculateAmount(instant, events, 30, false), 0.5);
    Near(SolarSchedule.CalculateAmount(instant.AddMinutes(15), events, 30, false), 1);
});
Test("Sunset fade is continuous and decreasing", () =>
{
    double last = 1;
    for (int second = -900; second <= 900; second += 10)
    {
        double next = SolarSchedule.CalculateAmount(set.At.AddSeconds(second), events, 30, true);
        Check(next <= last && next >= 0); last = next;
    }
    Near(last, 0);
});
Test("Zero-duration fade switches at the exact event", () =>
{
    Near(SolarSchedule.CalculateAmount(instant.AddTicks(-1), events, 0, false), 0);
    Near(SolarSchedule.CalculateAmount(instant, events, 0, false), 1);
    Near(SolarSchedule.CalculateAmount(set.At, events, 0, false), 0);
});
Test("Short polar day does not produce overlapping or out-of-range fades", () =>
{
    var brief = new[] { rise, rise with { At = instant.AddMinutes(20), IsSunrise = false } };
    double last = 0;
    for (int second = -1000; second <= 2400; second++)
    {
        double value = SolarSchedule.CalculateAmount(instant.AddSeconds(second), brief, 120, false);
        Check(value >= 0 && value <= 1 && Math.Abs(value - last) < 0.01); last = value;
    }
});
Test("Sunset offset moves transition center", () =>
{
    var day = SolarCalculator.GetDay(new DateOnly(2026, 10, 16), settings.Latitude!.Value, settings.Longitude!.Value, warsaw);
    var sunset = day.Events.Single(e => !e.IsSunrise);
    settings.SunsetOffsetMinutes = -45;
    Near(new SolarSchedule().Evaluate(sunset.At.AddMinutes(-45), settings).DayAmount, 0.5);
    settings.SunsetOffsetMinutes = 0;
});
Test("Daily rollover refreshes cached solar times", () =>
{
    var scheduler = new SolarSchedule();
    var day1 = scheduler.Evaluate(instant, settings);
    var day2 = scheduler.Evaluate(instant.AddDays(1), settings);
    Check(day1.Today.Date != day2.Today.Date && day1.Today.Events[0].At != day2.Today.Events[0].At);
});
Test("Location change invalidates cache", () =>
{
    var scheduler = new SolarSchedule();
    var first = scheduler.Evaluate(instant, settings).Today.Events[0].At;
    var moved = new AppSettings { Latitude = 50.0647, Longitude = 19.945, TimeZoneId = warsaw.Id };
    Check(scheduler.Evaluate(instant, moved).Today.Events[0].At != first);
});
Test("Interpolation clamps endpoints and rounds midpoint", () =>
{
    Check(SolarSchedule.Interpolate(25, 70, 0) == 25);
    Check(SolarSchedule.Interpolate(25, 70, 1) == 70);
    Check(SolarSchedule.Interpolate(25, 70, 0.5) == 48);
    Check(SolarSchedule.Interpolate(25, 70, -2) == 25);
});
Test("Reject non-finite coordinates", () =>
{
    foreach (double value in new[] { double.NaN, double.PositiveInfinity, 91 })
    {
        bool threw = false;
        try { SolarCalculator.ValidateCoordinates(value, 0); } catch (ArgumentOutOfRangeException) { threw = true; }
        Check(threw);
    }
});
Test("Invalid settings disable automatic control and clamp values", () =>
{
    var invalid = new AppSettings { Latitude = 95, Longitude = 0, AutoEnabled = true, TransitionMinutes = 999 };
    invalid.Monitors["test"] = new MonitorProfile { DayBrightness = 200, NightContrast = -20 };
    invalid.Normalize();
    Check(!invalid.AutoEnabled && !invalid.HasLocation && invalid.TransitionMinutes == 180);
    Check(invalid.Monitors["test"].DayBrightness == 100 && invalid.Monitors["test"].NightContrast == 0);
});
Test("Atomic save, round trip and corruption recovery", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "SolarDesk-test-" + Guid.NewGuid());
    Directory.CreateDirectory(directory);
    try
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        settings.AutoEnabled = true; store.Save(settings);
        settings.TransitionMinutes = 45; store.Save(settings);
        Check(store.Load().TransitionMinutes == 45 && !File.Exists(store.Path + ".tmp"));
        File.WriteAllText(store.Path, "{broken json");
        var recovered = store.Load();
        Check(!recovered.AutoEnabled && recovered.HasLocation && store.RecoveryMessage is not null);
        Check(recovered.TransitionMinutes == 30);
        store.Save(recovered);
        Check(File.ReadAllText(store.Path + ".bak").Contains("Latitude"));
    }
    finally { Directory.Delete(directory, true); }
});
Test("Denied location permission never reads coordinates", () =>
{
    var provider = new FakeLocationProvider(LocationPermission.Denied);
    bool rejected = false;
    try { LocationAcquisition.AcquireAsync(provider).GetAwaiter().GetResult(); }
    catch (LocationPermissionException) { rejected = true; }
    Check(rejected && provider.ReadCount == 0 && provider.PermissionCount == 1);
});
Test("Unspecified location permission never reads coordinates", () =>
{
    var provider = new FakeLocationProvider(LocationPermission.Unspecified);
    bool rejected = false;
    try { LocationAcquisition.AcquireAsync(provider).GetAwaiter().GetResult(); }
    catch (LocationPermissionException) { rejected = true; }
    Check(rejected && provider.ReadCount == 0);
});
Test("Allowed location reads once after permission completes", () =>
{
    var provider = new FakeLocationProvider(LocationPermission.Allowed);
    var result = LocationAcquisition.AcquireAsync(provider).GetAwaiter().GetResult();
    Check(provider.ReadCount == 1 && provider.PermissionCount == 1 && provider.PermissionBeforeRead);
    Near(result.Latitude, 52.2297); Near(result.Longitude, 21.0122);
});
Test("Location timeout is surfaced without a fallback request", () =>
{
    var provider = new FakeLocationProvider(LocationPermission.Allowed) { Failure = new TimeoutException() };
    bool rejected = false;
    try { LocationAcquisition.AcquireAsync(provider).GetAwaiter().GetResult(); }
    catch (TimeoutException) { rejected = true; }
    Check(rejected && provider.ReadCount == 1);
});
Test("Invalid provider coordinates are rejected", () =>
{
    var provider = new FakeLocationProvider(LocationPermission.Allowed) { Position = new DeviceLocation(double.NaN, 0, 1000) };
    bool rejected = false;
    try { LocationAcquisition.AcquireAsync(provider).GetAwaiter().GetResult(); }
    catch (ArgumentOutOfRangeException) { rejected = true; }
    Check(rejected);
});
Test("Vibrance endpoints and neutral use the driver's range", () =>
{
    foreach (var range in new[] { (0, 50, 100), (-100, 0, 100), (0, 32, 255) })
    {
        Check(VibranceScale.ToRaw(0, range.Item1, range.Item2, range.Item3) == range.Item1);
        Check(VibranceScale.ToRaw(50, range.Item1, range.Item2, range.Item3) == range.Item2);
        Check(VibranceScale.ToRaw(100, range.Item1, range.Item2, range.Item3) == range.Item3);
        Check(VibranceScale.ToPercent(range.Item2, range.Item1, range.Item2, range.Item3) == 50);
        int previous = range.Item1;
        for (int p = 0; p <= 100; p++)
        {
            int raw = VibranceScale.ToRaw(p, range.Item1, range.Item2, range.Item3);
            Check(raw >= previous && raw <= range.Item3);
            Near(VibranceScale.ToPercent(raw, range.Item1, range.Item2, range.Item3), p, 1);
            previous = raw;
        }
    }
    Check(VibranceScale.ToRaw(-10, -100, 0, 100) == -100);
    Check(VibranceScale.ToRaw(200, -100, 0, 100) == 100);
});
Test("New NVIDIA control preserves current color and separates original DDC values", () =>
{
    var profile = new MonitorProfile { DaySaturation = 100, NightSaturation = 0, OriginalSaturationRaw = 42 };
    profile.CaptureSaturation("nvidia", 61, null, 22);
    Check(profile.DaySaturation == 61 && profile.NightSaturation == 61);
    Check(profile.OriginalVibranceRaw == 22 && profile.OriginalSaturationRaw == 42);
    profile.DaySaturation = 70; profile.NightSaturation = 55;
    profile.CaptureSaturation("nvidia", 70, null, 40);
    Check(profile.DaySaturation == 70 && profile.NightSaturation == 55 && profile.OriginalVibranceRaw == 22);
    profile.CaptureSaturation(null, 0, null, null);
    Check(profile.SaturationSource == "nvidia" && profile.DaySaturation == 70);
    profile.CaptureSaturation("ddc", 45, 45, null);
    Check(profile.DaySaturation == 45 && profile.NightSaturation == 45 && profile.OriginalVibranceRaw == 22);
});
Test("Offline city lookup resolves known cities and handles the date line", () =>
{
    var warsawCity = CityCatalog.Nearest(52.2297, 21.0122);
    Check(warsawCity.City.Country == "PL" && warsawCity.DistanceKm < 5);
    Check(!CityCatalog.Label(52.2297, 21.0122).Contains('°'));
    var london = CityCatalog.Nearest(51.5074, -0.1278);
    Check(london.City.Country == "GB" && london.DistanceKm < 5);
    var fiji = CityCatalog.Nearest(-16.80, -179.99);
    Check(fiji.DistanceKm < 300, "Nearest city lookup must cross the international date line");
});
Test("City search accepts accents, returns a small list, and supplies valid time zones", () =>
{
    Check(CityCatalog.Search("a").Count == 0);
    var results = CityCatalog.Search("lodz");
    Check(results.Count is > 0 and <= 6 && results[0].Country == "PL");
    Check(CityCatalog.Search("Warsaw").Any(c => c.Country == "PL"));
    TimeZoneInfo.FindSystemTimeZoneById(results[0].WindowsTimeZoneId);
});
Test("Sync values applies the requested value to every linked display", () =>
{
    Check(LinkedControls.Apply([30, 60, 85], 1, 70, MonitorLinkMode.SameValues).SequenceEqual([70, 70, 70]));
    Check(LinkedControls.Apply([30, 60, 85], 2, 110, MonitorLinkMode.SameValues).SequenceEqual([100, 100, 100]));
});
Test("Sync offsets preserves differences in both directions", () =>
{
    var brighter = LinkedControls.Apply([30, 50, 70], 1, 60, MonitorLinkMode.KeepOffsets);
    Check(brighter.SequenceEqual([40, 60, 80]));
    Check(LinkedControls.Apply(brighter, 0, 25, MonitorLinkMode.KeepOffsets).SequenceEqual([25, 45, 65]));
});
Test("Offset groups stop together at limits and remain reversible", () =>
{
    var atTop = LinkedControls.Apply([30, 50, 70], 1, 100, MonitorLinkMode.KeepOffsets);
    Check(atTop.SequenceEqual([60, 80, 100]));
    Check(LinkedControls.Apply(atTop, 1, 50, MonitorLinkMode.KeepOffsets).SequenceEqual([30, 50, 70]));
    Check(LinkedControls.Apply([30, 50, 70], 1, 0, MonitorLinkMode.KeepOffsets).SequenceEqual([0, 20, 40]));
});
Test("Individual mode edits only the source and invalid mode resets safely", () =>
{
    Check(LinkedControls.Apply([30, 50, 70], 1, 60, MonitorLinkMode.Individual).SequenceEqual([30, 60, 70]));
    var invalid = new AppSettings { LinkMode = (MonitorLinkMode)99 };
    invalid.Normalize();
    Check(invalid.LinkMode == MonitorLinkMode.Individual);
    var saved = System.Text.Json.JsonSerializer.Serialize(new AppSettings { LinkMode = MonitorLinkMode.KeepOffsets });
    Check(System.Text.Json.JsonSerializer.Deserialize<AppSettings>(saved)!.LinkMode == MonitorLinkMode.KeepOffsets);
});
Test("Display shape recognizes ultrawide, widescreen and portrait resolutions", () =>
{
    Check(new DisplayShape(3440, 1440).Aspect == "21:9");
    Check(new DisplayShape(2560, 1080).Aspect == "21:9");
    Check(new DisplayShape(5120, 1440).Aspect == "32:9");
    Check(new DisplayShape(1920, 1080).Aspect == "16:9");
    Check(new DisplayShape(1080, 1920).Aspect == "9:16");
    Check(new DisplayShape(1920, 1200).Aspect == "16:10");
    Check(new DisplayShape(1280, 1024).Aspect == "5:4");
    Check(new DisplayShape(1234, 900).Aspect == "617:450");
    Check(!new DisplayShape(0, 0).IsKnown);
});
Test("Weather parses Celsius and negative snow temperatures", () =>
{
    var result = WeatherSnapshot.Parse("""{"current_units":{"temperature_2m":"°C"},"current":{"temperature_2m":-2.5,"weather_code":73,"is_day":0,"time":1792238400}}""");
    Check(result.Celsius == -2.5 && result.Effect == WeatherEffect.Snow && !result.IsDay);
    Check(result.IsFresh(result.At.AddMinutes(30)) && !result.IsFresh(result.At.AddHours(3)) && !result.IsFresh(result.At.AddHours(-1)));
});
Test("Missing, malformed and wrong-unit weather responses never become zero or clear weather", () =>
{
    string valid = """{"current_units":{"temperature_2m":"°C"},"current":{"temperature_2m":0,"weather_code":0,"is_day":1,"time":1792238400}}""";
    Check(WeatherSnapshot.Parse(valid).Celsius == 0);
    foreach (string invalid in new[] { "{}", "[]", "null", valid.Replace("°C", "°F"), valid.Replace("\"temperature_2m\":0", "\"temperature_2m\":null"), valid.Replace("\"weather_code\":0", "\"weather_code\":\"rain\""), valid.Replace("1792238400", "-1") })
    {
        bool rejected = false;
        try { WeatherSnapshot.Parse(invalid); } catch (System.Text.Json.JsonException) { rejected = true; }
        Check(rejected);
    }
});
Test("Weather effects distinguish rain, snow and unknown codes", () =>
{
    foreach (int code in new[] { 51, 61, 65, 80, 95, 99 }) Check(new WeatherSnapshot(10, code, true, instant).Effect == WeatherEffect.Rain);
    foreach (int code in new[] { 71, 73, 75, 77, 85, 86 }) Check(new WeatherSnapshot(-2, code, true, instant).Effect == WeatherEffect.Snow);
    Check(new WeatherSnapshot(10, 999, true, instant).Effect == WeatherEffect.None);
});
Test("Weather requests only send approximate coordinates using invariant formatting", () =>
{
    var previous = System.Globalization.CultureInfo.CurrentCulture;
    try
    {
        System.Globalization.CultureInfo.CurrentCulture = new("pl-PL");
        string uri = WeatherService.RequestUri(52.2297, 21.0122).AbsoluteUri;
        Check(uri.Contains("latitude=52.2&longitude=21.0&") && !uri.Contains("52.2297") && uri.StartsWith("https://api.open-meteo.com/"));
    }
    finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
});
Test("Absolute offsets restore baseline and clamp the whole group", () =>
{
    int[] baseline = [30, 50, 70];
    Check(LinkedControls.ApplyOffset(baseline, 10).Values.SequenceEqual([40, 60, 80]));
    Check(LinkedControls.ApplyOffset(baseline, 20).Values.SequenceEqual([50, 70, 90]));
    Check(LinkedControls.ApplyOffset(baseline, 0).Values.SequenceEqual(baseline));
    var upper = LinkedControls.ApplyOffset(baseline, 100);
    var lower = LinkedControls.ApplyOffset(baseline, -100);
    Check(upper.Offset == 30 && upper.Values.SequenceEqual([60, 80, 100]));
    Check(lower.Offset == -30 && lower.Values.SequenceEqual([0, 20, 40]));
    Check(LinkedControls.ApplyOffset([], 30).Values.Length == 0);
    Check(LinkedControls.ApplyOffset([0, 100], 30).Offset == 0);
});
Test("Offset state migrates safely and persists separate profile baselines", () =>
{
    var value = new AppSettings { DayOffsets = new OffsetProfile { Baselines = new() { ["main"] = new(60, 50, 50) }, Offsets = new(10, -5, 0) } };
    var copy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(value))!;
    copy.Normalize();
    Check(copy.DayOffsets!.Baselines["main"].Brightness == 60 && copy.DayOffsets.Offsets.Contrast == -5 && copy.NightOffsets is null);
    copy.DayOffsets.Offsets = new(500, 0, 0); copy.Normalize();
    Check(copy.DayOffsets is null);
    var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{}")!;
    Check(legacy.DayOffsets is null && legacy.DayRaysEnabled && legacy.SunRemindersEnabled);
});
Test("Sun reminders fire only in the delivery window and persist deduplication", () =>
{
    var sunset = new SolarEvent(DateTimeOffset.Parse("2026-10-17T18:00:00+02:00"), false);
    var config = new AppSettings { Latitude = 52, Longitude = 21, LocationName = "Warsaw", TimeZoneId = "Central European Standard Time", AutoEnabled = true };
    Check(SunReminders.FindDue(sunset.At.AddMinutes(-30).AddTicks(-1), config, [sunset]) is null);
    var due = SunReminders.FindDue(sunset.At.AddMinutes(-30), config, [sunset]);
    Check(due is not null && due.Title == "Sunset in 30 minutes" && due.Message.Contains("18:00") && due.Message.Contains("17:45–18:15"));
    Check(SunReminders.FindDue(sunset.At.AddMinutes(-29), config, [sunset]) is null);
    config.RecentSunReminders.Add(due!.Key);
    var reloaded = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(config))!;
    Check(SunReminders.FindDue(sunset.At.AddMinutes(-30).AddSeconds(15), reloaded, [sunset]) is null);
    config.RecentSunReminders.Clear(); config.SunRemindersEnabled = false;
    Check(SunReminders.FindDue(sunset.At.AddMinutes(-30), config, [sunset]) is null);
});
Test("Reminders describe paused, disabled and unavailable automation honestly", () =>
{
    var sunrise = new SolarEvent(DateTimeOffset.Parse("2026-10-17T07:00:00+02:00"), true);
    var config = new AppSettings { Latitude = 52, Longitude = 21, TimeZoneId = "Central European Standard Time" };
    var now = sunrise.At.AddMinutes(-30);
    Check(SunReminders.FindDue(now, config, [sunrise])!.Message.Contains("Auto is off"));
    config.AutoEnabled = true;
    Check(SunReminders.FindDue(now, config, [sunrise], temporarilyOverridden: true)!.Message.Contains("temporarily paused"));
    Check(SunReminders.FindDue(now, config, [sunrise], hasEligibleDisplays: false)!.Message.Contains("No connected displays"));
    config.SunriseOffsetMinutes = -60;
    Check(SunReminders.FindDue(now, config, [sunrise])!.Message.Contains("finished at 06:15"));
    config.SunriseOffsetMinutes = -20;
    Check(SunReminders.FindDue(now, config, [sunrise])!.Message.Contains("underway until 06:55"));
    config.TransitionMinutes = 0; config.SunriseOffsetMinutes = 10;
    Check(SunReminders.FindDue(now, config, [sunrise])!.Message.Contains("switch to your Day profile at 07:10"));
});
Test("Reminders use the city's date and time across midnight and DST", () =>
{
    var config = new AppSettings { Latitude = 52, Longitude = 21, TimeZoneId = "Central European Standard Time" };
    var midnight = new SolarEvent(DateTimeOffset.Parse("2026-10-18T00:10:00+02:00"), true);
    Check(SunReminders.FindDue(midnight.At.AddMinutes(-30), config, [midnight])!.Message.Contains("00:10 (18"));
    var winter = new SolarEvent(DateTimeOffset.Parse("2026-10-25T07:00:00+01:00"), true);
    Check(SunReminders.FindDue(winter.At.AddMinutes(-30), config, [winter])!.Message.Contains("07:00"));
    Check(SunReminders.FindDue(winter.At, config, []) is null);
    config.Latitude = null;
    Check(SunReminders.FindDue(midnight.At.AddMinutes(-30), config, [midnight]) is null);
});
Test("Cached reminder events ignore schedule offsets and include tomorrow", () =>
{
    var config = new AppSettings { Latitude = 52.2297, Longitude = 21.0122, TimeZoneId = "Central European Standard Time", SunriseOffsetMinutes = 35, SunsetOffsetMinutes = -20 };
    var now = DateTimeOffset.Parse("2026-10-17T12:00:00Z");
    var scheduler = new SolarSchedule();
    var events = scheduler.GetSolarEvents(now, config);
    var today = scheduler.Evaluate(now, config).Today;
    Check(today.Events.All(e => events.Contains(e)) && events.Any(e => e.At > now.AddDays(1)));
    config.SunriseOffsetMinutes = -90;
    Check(events.SequenceEqual(scheduler.GetSolarEvents(now, config)));
});
Test("All three modes remember independent Day and Night control values", () =>
{
    var config = new AppSettings { Monitors = new() {
        ["left"] = new() { DayBrightness = 30, DayContrast = 40, DaySaturation = 45, NightBrightness = 15, NightContrast = 35, NightSaturation = 40 },
        ["main"] = new() { DayBrightness = 70, DayContrast = 60, DaySaturation = 65, NightBrightness = 25, NightContrast = 45, NightSaturation = 55 }
    } };
    var individual = config.Monitors.ToDictionary(p => p.Key, p => DisplayProfileValues.Capture(p.Value));
    config.SelectMode(MonitorLinkMode.SameValues);
    Check(config.Monitors.All(p => DisplayProfileValues.Capture(p.Value) == individual[p.Key]), "First visit should inherit current values");
    var combined = new DisplayProfileValues(new(80, 53, 72), new(22, 43, 58), new(), new());
    foreach (var profile in config.Monitors.Values) combined.Apply(profile);
    config.SelectMode(MonitorLinkMode.Individual);
    Check(config.Monitors.All(p => DisplayProfileValues.Capture(p.Value) == individual[p.Key]));
    config.SelectMode(MonitorLinkMode.KeepOffsets);
    config.DayOffsets = new() { Baselines = individual.ToDictionary(p => p.Key, p => p.Value.Day), Offsets = new(10, 5, 0) };
    foreach (var profile in config.Monitors.Values) { profile.DayBrightness += 10; profile.DayContrast += 5; profile.NightSaturation += 3; }
    var offset = config.Monitors.ToDictionary(p => p.Key, p => DisplayProfileValues.Capture(p.Value));
    for (int round = 0; round < 3; round++)
    {
        config.SelectMode(MonitorLinkMode.SameValues);
        Check(config.Monitors.Values.All(p => DisplayProfileValues.Capture(p) == combined));
        config.SelectMode(MonitorLinkMode.Individual);
        Check(config.Monitors.All(p => DisplayProfileValues.Capture(p.Value) == individual[p.Key]));
        config.SelectMode(MonitorLinkMode.KeepOffsets);
        Check(config.Monitors.All(p => DisplayProfileValues.Capture(p.Value) == offset[p.Key]));
        Check(config.DayOffsets.Offsets.Brightness == 10 && config.DayOffsets.Baselines["left"].Brightness == 30);
    }
});
Test("Mode memories survive an actual settings save and reload", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "SolarDesk-mode-test-" + Guid.NewGuid());
    try
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var config = new AppSettings { Monitors = new() { ["main"] = new() { DayBrightness = 37, NightSaturation = 42 } } };
        config.SelectMode(MonitorLinkMode.SameValues);
        config.Monitors["main"].DayBrightness = 82; config.Monitors["main"].NightSaturation = 61;
        store.Save(config);
        var loaded = store.Load();
        Check(loaded.LinkMode == MonitorLinkMode.SameValues && loaded.Monitors["main"].DayBrightness == 82);
        loaded.SelectMode(MonitorLinkMode.Individual);
        Check(loaded.Monitors["main"].DayBrightness == 37 && loaded.Monitors["main"].NightSaturation == 42);
        loaded.SelectMode(MonitorLinkMode.SameValues);
        Check(loaded.Monitors["main"].DayBrightness == 82 && loaded.Monitors["main"].NightSaturation == 61);
    }
    finally
    {
        foreach (string name in new[] { "settings.json", "settings.json.bak", "settings.json.tmp" })
            File.Delete(Path.Combine(directory, name));
        if (Directory.Exists(directory)) Directory.Delete(directory);
    }
});
Test("Mode switches preserve hardware originals and monitor inclusion", () =>
{
    var monitor = new MonitorProfile { Enabled = false, AdjustContrast = false, OriginalBrightnessRaw = 42,
        OriginalContrastRaw = 46, OriginalVibranceRaw = 12, SaturationSource = "nvidia", ColorPresetRestorePending = true };
    var config = new AppSettings { Monitors = new() { ["main"] = monitor } };
    config.SelectMode(MonitorLinkMode.SameValues); monitor.DayBrightness = 90;
    config.SelectMode(MonitorLinkMode.Individual);
    Check(ReferenceEquals(config.Monitors["main"], monitor) && !monitor.Enabled && !monitor.AdjustContrast
        && monitor.OriginalBrightnessRaw == 42 && monitor.OriginalContrastRaw == 46 && monitor.OriginalVibranceRaw == 12
        && monitor.SaturationSource == "nvidia" && monitor.ColorPresetRestorePending);
});
Test("Legacy and malformed mode memories preserve current values", () =>
{
    var legacy = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("""{"LinkMode":1,"Monitors":{"main":{"DayBrightness":73,"NightSaturation":68}}}""")!;
    legacy.Normalize(); legacy.SelectMode(MonitorLinkMode.Individual);
    Check(legacy.Monitors["main"].DayBrightness == 73 && legacy.Monitors["main"].NightSaturation == 68);
    legacy.Monitors["main"].DayBrightness = 55;
    legacy.SelectMode(MonitorLinkMode.SameValues);
    Check(legacy.Monitors["main"].DayBrightness == 73);
    legacy.ModePresets[MonitorLinkMode.Individual].Monitors["main"] = new(new(999, 50, 50), new(25, 50, 50));
    legacy.ModePresets[(MonitorLinkMode)99] = new();
    legacy.Normalize(); legacy.SelectMode(MonitorLinkMode.Individual);
    Check(legacy.Monitors["main"].DayBrightness == 73 && !legacy.ModePresets.ContainsKey((MonitorLinkMode)99));
});
Test("New and disconnected displays keep independent remembered values", () =>
{
    var config = new AppSettings { Monitors = new() { ["main"] = new() { DayBrightness = 30 } } };
    config.SelectMode(MonitorLinkMode.SameValues); config.Monitors["main"].DayBrightness = 80;
    config.Monitors["new"] = new() { DayBrightness = 47 };
    config.SelectMode(MonitorLinkMode.Individual);
    Check(config.Monitors["main"].DayBrightness == 30 && config.Monitors["new"].DayBrightness == 47);
    config.Monitors["new"].DayBrightness = 59;
    config.SelectMode(MonitorLinkMode.SameValues);
    Check(config.Monitors["main"].DayBrightness == 80 && config.Monitors["new"].DayBrightness == 47);
    // Disconnected profiles remain in Monitors, so their per-mode values must survive every switch.
    config.SelectMode(MonitorLinkMode.KeepOffsets); config.Monitors["new"].DayBrightness = 62;
    config.SelectMode(MonitorLinkMode.Individual);
    Check(config.Monitors["new"].DayBrightness == 59);
    config.SelectMode(MonitorLinkMode.KeepOffsets);
    Check(config.Monitors["new"].DayBrightness == 62);
});
Test("Additional settings preserve separate modes and the full brightness range", () =>
{
    var profile = new MonitorProfile { DayAdvanced = new(Sharpness: 5, Gamma: 0x7800) };
    var config = new AppSettings { Monitors = new() { ["main"] = profile } };
    config.SelectMode(MonitorLinkMode.SameValues); profile.DayAdvanced = new(Sharpness: 9); profile.DayBrightness = 90;
    config.SelectMode(MonitorLinkMode.Individual); Check(profile.DayAdvanced.Sharpness == 5);
    config.SelectMode(MonitorLinkMode.SameValues); Check(profile.DayAdvanced.Sharpness == 9 && profile.DayBrightness == 90);
});
Test("Capability parser isolates feature codes from their values", () =>
{
    var parsed = DdcCapabilities.Parse("(model(Test)vcp(10 14(01 03 05) 72(00 78 FB 50 64 78) 87)mccs_ver(2.2))");
    Check(parsed.ContainsKey(0x87) && !parsed.ContainsKey(0x78) && parsed[0x72].SequenceEqual(new byte[] { 0, 0x78, 0xfb, 0x50, 0x64, 0x78 }));
    Check(DdcCapabilities.Parse("(vcp(10 72(00 78 FF)").Count == 0);
});
Test("Gamma choices use MCCS two-byte encoding and supported ranges", () =>
{
    var choices = DdcCapabilities.GammaChoices([0, 0x78, 0xfb, 0x64, 0x78]);
    Check(choices.Select(c => c.Value).SequenceEqual(new uint[] { 0x6400, 0x7800 }) && choices[1].Label == "2.20");
    var relative = DdcCapabilities.GammaChoices([0xff, 0x78, 0xfc, 3, 0x12]);
    Check(relative.First().Value == 0x0304 && relative.Last().Value == 0x1204 && relative.Length == 6);
    Check(DdcCapabilities.GammaChoices([0, 0x78, 0xfd, 100, 80]).Length == 0);
    Check(DdcCapabilities.GammaChoices([0, 0x78, 0xff, 1]).Length == 0);
});
Test("Capability groups can touch without losing features or treating choices as codes", () =>
{
    var parsed = DdcCapabilities.Parse("(vcp(10 14(01 04 05 08 0B)16 18 CC(01 02 03 04 87)D6(01 05) DF))");
    Check(parsed.Keys.Order().SequenceEqual(new byte[] { 0x10, 0x14, 0x16, 0x18, 0xcc, 0xd6, 0xdf }));
    Check(parsed[0xcc].SequenceEqual(new byte[] { 1, 2, 3, 4, 0x87 }) && parsed[0xd6].SequenceEqual(new byte[] { 1, 5 }));
});
Test("Malformed capability groups never leak their values into feature codes", () =>
{
    var parsed = DdcCapabilities.Parse("(vcp(10 CC(01 invalid 87)D6(01 05) 12))");
    Check(parsed.Keys.Order().SequenceEqual(new byte[] { 0x10, 0x12, 0xd6 }));
    var nested = DdcCapabilities.Parse("(vcp(10 CC(01 (87) 04)D6(01 05)))");
    Check(nested.Keys.Order().SequenceEqual(new byte[] { 0x10, 0xd6 }));
});
Test("Warmth and RGB intent stay exclusive and originals remain unchanged", () =>
{
    var profile = new MonitorProfile();
    var controls = new AdvancedControl[] { new(AdvancedKind.Temperature, "Warmth", 6500, 3000, 30000, []), new(AdvancedKind.Red, "Red", 47, 0, 100, []) };
    profile.CaptureAdvanced(controls);
    Check(profile.OriginalAdvanced.Temperature == 6500 && profile.OriginalAdvanced.Red == 47 && profile.DayAdvanced.Red is null);
    profile.DayAdvanced = profile.DayAdvanced.Set(AdvancedKind.Red, 40); profile.AdvancedManaged = true;
    profile.CaptureAdvanced([controls[1] with { Current = 40 }]);
    Check(profile.DayAdvanced.Temperature is null && profile.OriginalAdvanced.Red == 47);
});
Test("Native warmth and gamma choices require valid advertised values", () =>
{
    var warmth = DdcCapabilities.TemperatureChoices(100, 35, 70);
    Check(warmth.Any(c => c.Value == 6500) && warmth.All(c => c.Value <= 10000));
    Check(DdcCapabilities.TemperatureChoices(0, 35, 100).Length == 0);
    Check(DdcCapabilities.TemperatureChoices(100, 101, 100).Length == 0);
    var gamma = DdcCapabilities.GammaChoices([1, 2, 3]);
    Check(gamma.Length == 3 && gamma[1].Value == 2 && gamma[1].Label == "Mode 2");
    var nativeGamma = DdcCapabilities.GammaChoices([0x50, 0x64, 0x78, 0x8c, 0xa0]);
    Check(nativeGamma[2].Value == 0x78 && nativeGamma[2].Label == "Mode 3");
});
Test("Presets and advanced values survive settings serialization", () =>
{
    var profile = new MonitorProfile { DayAdvanced = new(Gamma: 0x7800),
        Presets = [new() { Name = "Reading", Hotkey = 2, Values = new(45, 50, 40), Advanced = new(Temperature: 5000) }] };
    var json = System.Text.Json.JsonSerializer.Serialize(profile);
    var loaded = System.Text.Json.JsonSerializer.Deserialize<MonitorProfile>(json)!; loaded.Normalize();
    Check(loaded.Presets.Single().Hotkey == 2 && loaded.Presets.Single().Advanced.Temperature == 5000 && loaded.DayAdvanced.Gamma == 0x7800);
});
Test("Legacy brightness limits are ignored and removed without changing saved profiles", () =>
{
    const string json = """
        {"MinimumBrightness":20,"MaximumBrightness":75,"DayBrightness":95,"NightBrightness":5,
         "DayAdvanced":{"Gamma":120},"Presets":[{"Name":"Bright","Values":{"Brightness":100,"Contrast":50,"Saturation":50}}]}
        """;
    var profile = System.Text.Json.JsonSerializer.Deserialize<MonitorProfile>(json)!;
    profile.Normalize();
    Check(profile.DayBrightness == 95 && profile.NightBrightness == 5 && profile.DayAdvanced.Gamma == 120);
    Check(profile.Presets.Single().Values.Brightness == 100);
    var saved = System.Text.Json.JsonSerializer.Serialize(profile);
    Check(!saved.Contains("MinimumBrightness") && !saved.Contains("MaximumBrightness"));
    profile.DayBrightness = 150; profile.NightBrightness = -10; profile.Normalize();
    Check(profile.DayBrightness == 100 && profile.NightBrightness == 0);
});
Test("The selected profile skin survives restart independently of automation", () =>
{
    var config = new AppSettings { SelectedNightProfile = true, AutoEnabled = false, Monitors = new() { ["main"] = new() { DayBrightness = 70, NightBrightness = 20 } } };
    var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(config))!;
    restored.Normalize();
    Check(restored.SelectedNightProfile && !restored.AutoEnabled && restored.Monitors["main"].DayBrightness == 70 && restored.Monitors["main"].NightBrightness == 20);
});
Test("Manual values and appearance survive save and reload without changing Day or Night", () =>
{
    string directory = Path.Combine(Path.GetTempPath(), "SolarDesk-manual-test-" + Guid.NewGuid());
    try
    {
        var store = new SettingsStore(Path.Combine(directory, "settings.json"));
        var config = new AppSettings { Appearance = AppAppearance.Night, Monitors = new() { ["main"] = new() { DayBrightness = 80, NightBrightness = 20 } } };
        var profile = config.Monitors["main"];
        profile.EnsureManual(new(41, 52, 63), new() { Sharpness = 7 });
        profile.EnsureManual(new(99, 99, 99), new());
        foreach (var mode in new[] { MonitorLinkMode.SameValues, MonitorLinkMode.KeepOffsets })
        {
            config.SelectMode(mode);
            profile.Manual = new(41 + (int)mode, 52, 63);
            profile.ManualAdvanced = new() { Sharpness = 7 + (uint)mode };
        }
        store.Save(config);
        var loaded = store.Load();
        Check(loaded.ManualProfileSelected && !loaded.AutoEnabled && loaded.Appearance == AppAppearance.Night);
        foreach (var mode in Enum.GetValues<MonitorLinkMode>())
        {
            loaded.SelectMode(mode);
            var p = loaded.Monitors["main"];
            Check(p.Manual == new ControlValues(41 + (int)mode, 52, 63) && p.ManualAdvanced.Sharpness == 7 + (uint)mode);
            Check(p.DayBrightness == 80 && p.NightBrightness == 20);
        }
    }
    finally { Directory.Delete(directory, true); }
});
Test("Manual migration leaves old Day and Night profiles untouched and rejects invalid values", () =>
{
    var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>("{\"SelectedNightProfile\":true,\"Monitors\":{\"main\":{\"DayBrightness\":72,\"NightBrightness\":18}}}")!;
    restored.Normalize();
    Check(restored.ManualProfileSelected && restored.Monitors["main"].Manual is null && restored.Monitors["main"].DayBrightness == 72 && restored.Monitors["main"].NightBrightness == 18);
    restored.Monitors["main"].Manual = new(-1, 101, 50);
    restored.Appearance = (AppAppearance)99;
    restored.Normalize();
    Check(restored.Monitors["main"].Manual is null && restored.Appearance == AppAppearance.FollowProfile);
});
Test("Edits expire at the next sun transition, not an arbitrary one-hour timeout", () =>
{
    var config = new AppSettings { Latitude = 52.2297, Longitude = 21.0122, TimeZoneId = warsaw.Id, TransitionMinutes = 30, SunsetOffsetMinutes = 10 };
    var scheduler = new SolarSchedule();
    var noon = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    var sunset = scheduler.Evaluate(noon, config).NextEvent!;
    Check(!sunset.IsSunrise && scheduler.NextTransitionBoundary(noon, config) == sunset.At.AddMinutes(-15));
    Check(scheduler.NextTransitionBoundary(sunset.At, config) == sunset.At.AddMinutes(15));
    config.TransitionMinutes = 0;
    Check(scheduler.NextTransitionBoundary(noon, config) == sunset.At);
});
Test("Background scheduling sleeps when idle and wakes at fade boundaries", () =>
{
    var config = new AppSettings { Latitude = 52.2297, Longitude = 21.0122, TimeZoneId = warsaw.Id,
        AutoEnabled = true, TransitionMinutes = 30, SunRemindersEnabled = false };
    var scheduler = new SolarSchedule();
    var noon = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    var sunset = scheduler.Evaluate(noon, config).NextEvent!.At;
    TimeSpan Delay(DateTimeOffset at, bool foreground = false, bool manual = false,
        DateTimeOffset? until = null, DateTimeOffset? paused = null)
        => scheduler.NextUpdateDelay(at, config, foreground, manual, until, paused);
    Check(Delay(noon).TotalSeconds == 60 && Delay(noon, foreground: true).TotalSeconds == 10);
    Check(Delay(sunset.AddMinutes(-15).AddSeconds(-3)).TotalSeconds == 3);
    Check(Delay(sunset.AddMinutes(-15)).TotalSeconds == 10);
    Check(Delay(sunset).TotalSeconds == 10 && Delay(sunset.AddMinutes(15)).TotalSeconds == 60);
    Check(Delay(sunset.AddMinutes(15).AddSeconds(-2)).TotalSeconds == 2);
    Check(Delay(sunset, manual: true).TotalSeconds == 60);
    Check(Delay(sunset, manual: true, until: sunset.AddSeconds(4)).TotalSeconds == 4);
    Check(Delay(sunset, paused: sunset.AddSeconds(7)).TotalSeconds == 7);
    config.TransitionMinutes = 0;
    Check(Delay(sunset.AddSeconds(-2)).TotalSeconds == 2 && Delay(sunset).TotalSeconds == 60);
    config.AutoEnabled = false;
    Check(Delay(sunset.AddSeconds(-2)).TotalSeconds == 60);
});
Test("Background scheduling preserves reminder windows, deadlines and polar operation", () =>
{
    var config = new AppSettings { Latitude = 52.2297, Longitude = 21.0122, TimeZoneId = warsaw.Id,
        AutoEnabled = false, SunRemindersEnabled = true, SunsetOffsetMinutes = 60 };
    var scheduler = new SolarSchedule();
    var noon = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero);
    var due = scheduler.GetSolarEvents(noon, config).First(e => e.At > noon).At.AddMinutes(-30);
    TimeSpan Delay(DateTimeOffset at) => scheduler.NextUpdateDelay(at, config, false, false, null, null);
    Check(Delay(due.AddSeconds(-5)).TotalSeconds == 5);
    Check(Delay(due).TotalSeconds == 10 && Delay(due.AddSeconds(59)).TotalSeconds == 10);
    Check(Delay(due.AddMinutes(1)).TotalSeconds == 60);
    Check(scheduler.NextUpdateDelay(noon, config, false, true, noon.AddSeconds(-1), null).TotalMilliseconds == 100);
    config.Latitude = 90; config.TimeZoneId = "UTC"; config.AutoEnabled = true;
    Check(Delay(new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.Zero)).TotalSeconds == 60);
    config.Latitude = null; config.Longitude = null;
    Check(Delay(noon).TotalSeconds == 60);
});
Console.WriteLine($"\n{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;

sealed class FakeLocationProvider(LocationPermission permission) : IDeviceLocationProvider
{
    public int PermissionCount { get; private set; }
    public int ReadCount { get; private set; }
    public bool PermissionBeforeRead { get; private set; }
    public Exception? Failure { get; init; }
    public DeviceLocation Position { get; init; } = new(52.2297, 21.0122, 1000);
    public Task<LocationPermission> RequestPermissionAsync() { PermissionCount++; return Task.FromResult(permission); }
    public Task<DeviceLocation> GetLocationAsync()
    {
        PermissionBeforeRead = PermissionCount > 0;
        ReadCount++;
        return Failure is null ? Task.FromResult(Position) : Task.FromException<DeviceLocation>(Failure);
    }
}
