using System.ComponentModel;
using System.Windows;

namespace SolarDesk;

public sealed partial class MonitorViewModel
{
    private List<AdvancedControlViewModel>? advancedControls;
    private AdvancedValues? latestAdvanced;
    public List<AdvancedControlViewModel> AdvancedControls => advancedControls ??= (Reading.Advanced ?? []).Select(c => new AdvancedControlViewModel(this, c)).ToList();
    public bool HasAdvancedControls => AdvancedControls.Count > 0;
    public string AdditionalHint => HasAdvancedControls ? "For this display · " + SelectedProfileName + " profile"
        : "This display does not expose extra picture controls. Presets remain available.";
    public string UnavailableAdvancedText
    {
        get
        {
            var missing = new[] { (AdvancedKind.Sharpness, "sharpness"), (AdvancedKind.Temperature, "warmth"), (AdvancedKind.Gamma, "gamma") }
                .Where(item => !AdvancedControls.Any(c => c.Control.Kind == item.Item1)).Select(item => item.Item2).ToArray();
            return missing.Length == 0 ? "" : "Not available on this display: " + string.Join(", ", missing) + ".";
        }
    }
    public AdvancedValues SelectedAdvanced => IsNightProfile ? Profile.NightAdvanced : Profile.DayAdvanced;
    public uint AdvancedValue(AdvancedKind kind, uint fallback) => SelectedAdvanced.Get(kind) ?? latestAdvanced?.Get(kind) ?? fallback;
    public void SetAdvanced(AdvancedControl control, uint value)
    {
        if (!control.Accepts(value)) return;
        if (SelectedAdvanced.Get(control.Kind) == value) return;
        Profile.AdvancedManaged = true;
        var next = SelectedAdvanced;
        if (control.Kind is AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue)
            foreach (var rgb in AdvancedControls.Where(c => c.Control.Kind is AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue))
                next = next.Set(rgb.Control.Kind, (uint)rgb.Value);
        next = next.Set(control.Kind, value);
        if (IsNightProfile) Profile.NightAdvanced = next; else Profile.DayAdvanced = next;
        Changed(IsNightProfile ? "NightAdvanced" : "DayAdvanced");
        RefreshAdditional();
    }
    public void UpdateAdvancedReading(AdvancedValues? values)
    {
        if (values is null || values == latestAdvanced) return;
        latestAdvanced = values;
        RefreshAdditional();
    }
    public void RefreshAdditional()
    {
        if (advancedControls is not null) foreach (var control in advancedControls) control.Refresh();
        OnPropertyChanged(nameof(AdditionalHint));
    }
    public string PresetName { get; set; } = "";
    public int PresetHotkey { get; set; }
    public MonitorPreset? SelectedPreset { get; set; }
    public IEnumerable<MonitorPreset> Presets => Profile.Presets.ToArray();
    public static string[] HotkeyLabels => ["No shortcut", .. Enumerable.Range(1, 9).Select(i => "Ctrl + Alt + Shift + " + i)];
    public void RefreshPresets() { OnPropertyChanged(nameof(Presets)); OnPropertyChanged(nameof(SelectedPreset)); OnPropertyChanged(nameof(PresetName)); OnPropertyChanged(nameof(PresetHotkey)); }
    public void LoadPresetEditor(MonitorPreset preset) { SelectedPreset = preset; PresetName = preset.Name; PresetHotkey = preset.Hotkey; OnPropertyChanged(nameof(PresetName)); OnPropertyChanged(nameof(PresetHotkey)); }
}

public sealed class AdvancedControlViewModel(MonitorViewModel owner, AdvancedControl control) : INotifyPropertyChanged
{
    public AdvancedControl Control => control;
    public string Title => control.Title;
    public double Minimum => control.Minimum;
    public double Maximum => control.Maximum;
    public ControlChoice[] Choices => control.Choices;
    public Visibility SliderVisibility => Choices.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ChoiceVisibility => Choices.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public double Value { get => owner.AdvancedValue(control.Kind, control.Current); set { if (Choices.Length == 0) owner.SetAdvanced(control, (uint)Math.Clamp(Math.Round(value), Minimum, Maximum)); } }
    public uint? Choice { get => control.Kind == AdvancedKind.Temperature && owner.SelectedAdvanced.Temperature is null
        && (owner.SelectedAdvanced.Red.HasValue || owner.SelectedAdvanced.Green.HasValue || owner.SelectedAdvanced.Blue.HasValue)
        ? null : owner.AdvancedValue(control.Kind, control.Current); set { if (value is uint selected) owner.SetAdvanced(control, selected); } }
    public string ValueText => control.Kind == AdvancedKind.Temperature && owner.SelectedAdvanced.Temperature is null
        && (owner.SelectedAdvanced.Red.HasValue || owner.SelectedAdvanced.Green.HasValue || owner.SelectedAdvanced.Blue.HasValue) ? "Custom RGB"
        : Choices.FirstOrDefault(c => c.Value == (uint)Value)?.Label ?? (Choices.Length > 0 ? "Current" : Value.ToString("0"));
    public string Hint => control.Kind == AdvancedKind.Temperature ? "Lower Kelvin is warmer. A warmth preset replaces custom RGB balance."
        : control.Kind is AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue ? "Custom RGB balance replaces the warmth preset."
        : "Uses the range reported by this display.";
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Refresh() { foreach (string name in new[] { nameof(Value), nameof(Choice), nameof(ValueText) }) PropertyChanged?.Invoke(this, new(name)); }
}
