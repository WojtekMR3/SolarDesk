namespace SolarDesk.Core;

public sealed record DisplayProfileValues(ControlValues Day, ControlValues Night, AdvancedValues? DayAdvanced = null, AdvancedValues? NightAdvanced = null,
    ControlValues? Manual = null, AdvancedValues? ManualAdvanced = null)
{
    public static DisplayProfileValues Capture(MonitorProfile profile) => new(
        new(profile.DayBrightness, profile.DayContrast, profile.DaySaturation),
        new(profile.NightBrightness, profile.NightContrast, profile.NightSaturation), profile.DayAdvanced, profile.NightAdvanced,
        profile.Manual, profile.Manual is null ? null : profile.ManualAdvanced);

    public bool IsValid() => Day is not null && Night is not null && Day.InRange(0, 100) && Night.InRange(0, 100)
        && (DayAdvanced?.IsValid() ?? true) && (NightAdvanced?.IsValid() ?? true) && (Manual?.InRange(0, 100) ?? true) && (ManualAdvanced?.IsValid() ?? true);

    public void Apply(MonitorProfile profile)
    {
        profile.Manual = Manual;
        profile.ManualAdvanced = ManualAdvanced ?? new();
        profile.DayBrightness = Day.Brightness;
        profile.DayContrast = Day.Contrast;
        profile.DaySaturation = Day.Saturation;
        profile.NightBrightness = Night.Brightness;
        profile.NightContrast = Night.Contrast;
        profile.NightSaturation = Night.Saturation;
        profile.DayAdvanced = DayAdvanced ?? profile.OriginalAdvanced;
        profile.NightAdvanced = NightAdvanced ?? profile.OriginalAdvanced;
    }
}

public sealed class ModePreset
{
    public Dictionary<string, DisplayProfileValues> Monitors { get; set; } = [];

    public void Normalize()
    {
        Monitors ??= [];
        foreach (string id in Monitors.Keys.ToArray())
            if (Monitors[id] is null || !Monitors[id].IsValid()) Monitors.Remove(id);
    }
}
