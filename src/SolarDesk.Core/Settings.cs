using System.Text.Json;

namespace SolarDesk.Core;

public enum AppAppearance { FollowProfile, Day, Night }

public sealed class MonitorProfile
{
    public ControlValues? Manual { get; set; }
    public AdvancedValues ManualAdvanced { get; set; } = new();
    public void EnsureManual(ControlValues values, AdvancedValues advanced)
    {
        if (Manual is not null) return;
        Manual = values;
        ManualAdvanced = advanced;
    }
    public bool Enabled { get; set; } = true;
    public bool AdjustContrast { get; set; } = true;
    public int DayBrightness { get; set; } = 65;
    public int NightBrightness { get; set; } = 25;
    public int DayContrast { get; set; } = 50;
    public int NightContrast { get; set; } = 50;
    public int DaySaturation { get; set; } = 50;
    public int NightSaturation { get; set; } = 50;
    public uint? OriginalBrightnessRaw { get; set; }
    public uint? OriginalContrastRaw { get; set; }
    public uint? OriginalSaturationRaw { get; set; }
    public int? OriginalVibranceRaw { get; set; }
    public string? SaturationSource { get; set; }
    public uint? OriginalColorPresetRaw { get; set; }
    public bool ColorPresetRestorePending { get; set; }
    public bool AdvancedManaged { get; set; }
    public AdvancedValues DayAdvanced { get; set; } = new();
    public AdvancedValues NightAdvanced { get; set; } = new();
    public AdvancedValues OriginalAdvanced { get; set; } = new();
    public List<MonitorPreset> Presets { get; set; } = [];

    public void CaptureAdvanced(IEnumerable<AdvancedControl> controls)
    {
        foreach (var control in controls)
        {
            if (OriginalAdvanced.Get(control.Kind).HasValue || control.Kind == AdvancedKind.Temperature && control.Current == 0) continue;
            OriginalAdvanced = OriginalAdvanced.SetRaw(control.Kind, control.Current);
        }
        if (!AdvancedManaged) DayAdvanced = NightAdvanced = OriginalAdvanced.Temperature is uint temperature
            ? OriginalAdvanced.Set(AdvancedKind.Temperature, temperature) : OriginalAdvanced;
    }

    public void CaptureSaturation(string? source, int percent, uint? ddcRaw, int? vibranceRaw)
    {
        if (source is null) return;
        bool firstRead = source == "nvidia" ? OriginalVibranceRaw is null : OriginalSaturationRaw is null;
        // Old values may refer to color presets or to a different backend's range.
        // Begin from the actual current color setting when discovering a new backend.
        if (source != SaturationSource || firstRead)
            DaySaturation = NightSaturation = Math.Clamp(percent, 0, 100);
        if (source == "nvidia") OriginalVibranceRaw ??= vibranceRaw;
        else OriginalSaturationRaw ??= ddcRaw;
        SaturationSource = source;
    }

    public void Normalize()
    {
        if (Manual is not null && !Manual.InRange(0, 100)) Manual = null;
        if (ManualAdvanced is null || !ManualAdvanced.IsValid()) ManualAdvanced = new();
        DayBrightness = Math.Clamp(DayBrightness, 0, 100);
        NightBrightness = Math.Clamp(NightBrightness, 0, 100);
        DayContrast = Math.Clamp(DayContrast, 0, 100);
        NightContrast = Math.Clamp(NightContrast, 0, 100);
        DaySaturation = Math.Clamp(DaySaturation, 0, 100);
        NightSaturation = Math.Clamp(NightSaturation, 0, 100);
        if (DayAdvanced is null || !DayAdvanced.IsValid()) DayAdvanced = new();
        if (NightAdvanced is null || !NightAdvanced.IsValid()) NightAdvanced = new();
        if (OriginalAdvanced is null || !OriginalAdvanced.IsValid()) OriginalAdvanced = new();
        Presets = (Presets ?? []).Where(p => p is not null && p.Values is not null && p.Values.InRange(0, 100)
            && p.Advanced is not null && p.Advanced.IsValid() && !string.IsNullOrWhiteSpace(p.Name)).Take(30).ToList();
        foreach (var preset in Presets) { preset.Name = preset.Name.Trim()[..Math.Min(40, preset.Name.Trim().Length)]; preset.Hotkey = Math.Clamp(preset.Hotkey, 0, 9); }
    }
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string LocationName { get; set; } = "Choose your location";
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
    public int TransitionMinutes { get; set; } = 30;
    public int SunriseOffsetMinutes { get; set; }
    public int SunsetOffsetMinutes { get; set; }
    public bool AutoEnabled { get; set; }
    public bool SelectedNightProfile { get; set; }
    public bool ManualProfileSelected { get; set; } = true;
    public AppAppearance Appearance { get; set; }
    public bool WeatherEnabled { get; set; } = true;
    public bool WeatherEffectsEnabled { get; set; } = true;
    public bool DayRaysEnabled { get; set; } = true;
    public bool SunRemindersEnabled { get; set; } = true;
    public List<string> RecentSunReminders { get; set; } = [];
    public bool LocationPromptDismissed { get; set; }
    public MonitorLinkMode LinkMode { get; set; }
    public Dictionary<MonitorLinkMode, ModePreset> ModePresets { get; set; } = [];
    public OffsetProfile? DayOffsets { get; set; }
    public OffsetProfile? NightOffsets { get; set; }
    public OffsetProfile? ManualOffsets { get; set; }
    public Dictionary<string, MonitorProfile> Monitors { get; set; } = [];

    public void RememberCurrentMode()
    {
        if (!ModePresets.TryGetValue(LinkMode, out var preset)) ModePresets[LinkMode] = preset = new();
        foreach (var (id, profile) in Monitors) preset.Monitors[id] = DisplayProfileValues.Capture(profile);
    }

    public void SelectMode(MonitorLinkMode next)
    {
        if (!Enum.IsDefined(next)) throw new ArgumentOutOfRangeException(nameof(next));
        if (next == LinkMode) return;
        RememberCurrentMode();
        if (!ModePresets.TryGetValue(next, out var preset)) ModePresets[next] = preset = new();
        foreach (var (id, profile) in Monitors)
        {
            // Seed only missing presets/displays. Existing modes keep independent Day and Night values.
            if (!preset.Monitors.TryGetValue(id, out var saved)) preset.Monitors[id] = DisplayProfileValues.Capture(profile);
            else saved.Apply(profile);
        }
        LinkMode = next;
    }
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasLocation => Latitude is double lat && Longitude is double lon && double.IsFinite(lat) && double.IsFinite(lon)
        && lat >= -90 && lat <= 90 && lon >= -180 && lon <= 180;

    public void Normalize()
    {
        if (!HasLocation) { Latitude = null; Longitude = null; AutoEnabled = false; }
        try { TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId); }
        catch { TimeZoneId = TimeZoneInfo.Local.Id; AutoEnabled = false; }
        TransitionMinutes = Math.Clamp(TransitionMinutes, 0, 180);
        SunriseOffsetMinutes = Math.Clamp(SunriseOffsetMinutes, -120, 120);
        SunsetOffsetMinutes = Math.Clamp(SunsetOffsetMinutes, -120, 120);
        if (!Enum.IsDefined(Appearance)) Appearance = AppAppearance.FollowProfile;
        if (ManualOffsets is not null && !ManualOffsets.IsValid()) ManualOffsets = null;
        if (!Enum.IsDefined(LinkMode)) LinkMode = MonitorLinkMode.Individual;
        ModePresets ??= [];
        foreach (var mode in ModePresets.Keys.ToArray())
            if (!Enum.IsDefined(mode) || ModePresets[mode] is null) ModePresets.Remove(mode);
            else ModePresets[mode].Normalize();
        if (DayOffsets is not null && !DayOffsets.IsValid()) DayOffsets = null;
        if (NightOffsets is not null && !NightOffsets.IsValid()) NightOffsets = null;
        RecentSunReminders = (RecentSunReminders ?? []).Where(key => !string.IsNullOrWhiteSpace(key)).Distinct().TakeLast(8).ToList();
        Monitors ??= [];
        foreach (string key in Monitors.Keys.ToArray())
            if (Monitors[key] is null) Monitors.Remove(key); else Monitors[key].Normalize();
        if (string.IsNullOrWhiteSpace(LocationName)) LocationName = "Saved location";
    }
}

public sealed class SettingsStore(string path)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    public string Path { get; } = path;
    public string? RecoveryMessage { get; private set; }

    public AppSettings Load()
    {
        if (!File.Exists(Path)) return new AppSettings();
        try { return Read(Path); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or NotSupportedException)
        {
            RecoveryMessage = "Settings could not be read. Automation is paused; review your settings.";
            try
            {
                var recovered = Read(Path + ".bak");
                recovered.AutoEnabled = false;
                return recovered;
            }
            catch { return new AppSettings(); }
        }
    }

    private static AppSettings Read(string file)
    {
        var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(file)) ?? throw new JsonException("Empty settings.");
        if (settings.Version != 1) throw new NotSupportedException("Unsupported settings version.");
        settings.Normalize();
        return settings;
    }

    public void Save(AppSettings settings)
    {
        settings.Normalize();
        settings.RememberCurrentMode();
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        string temporary = Path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
        if (File.Exists(Path))
        {
            // Never replace a valid recovery backup with a corrupt previous file.
            try { Read(Path); File.Copy(Path, Path + ".bak", true); }
            catch (Exception e) when (e is JsonException or NotSupportedException) { }
        }
        File.Move(temporary, Path, true);
    }
}
