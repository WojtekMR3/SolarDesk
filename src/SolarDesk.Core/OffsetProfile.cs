namespace SolarDesk.Core;

public sealed record ControlValues(int Brightness, int Contrast, int Saturation)
{
    public int Get(string control) => control switch
    {
        "Brightness" => Brightness, "Contrast" => Contrast, "Saturation" => Saturation,
        _ => throw new ArgumentException("Unknown control.", nameof(control))
    };
    public bool InRange(int minimum, int maximum) => Brightness >= minimum && Brightness <= maximum
        && Contrast >= minimum && Contrast <= maximum && Saturation >= minimum && Saturation <= maximum;
}

public sealed class OffsetProfile
{
    public Dictionary<string, ControlValues> Baselines { get; set; } = [];
    public ControlValues Offsets { get; set; } = new(0, 0, 0);
    public bool IsValid() => Baselines is not null && Offsets is not null && Offsets.InRange(-100, 100)
        && Baselines.Values.All(value => value is not null && value.InRange(0, 100));
    public void SetOffset(string control, int value) => Offsets = control switch
    {
        "Brightness" => Offsets with { Brightness = value },
        "Contrast" => Offsets with { Contrast = value },
        "Saturation" => Offsets with { Saturation = value },
        _ => throw new ArgumentException("Unknown control.", nameof(control))
    };
}
