using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SolarDesk;

public sealed partial class MonitorViewModel(MonitorReading reading, MonitorProfile profile, Action<MonitorViewModel, string, int?> changed) : INotifyPropertyChanged
{
    private string status = reading.Status;
    private int currentBrightness = reading.BrightnessPercent;
    private bool isNightProfile;
    private bool isSharedView;
    public bool IsSharedView
    {
        get => isSharedView;
        set { if (isSharedView == value) return; isSharedView = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsIndividualView)); }
    }
    public bool IsIndividualView => !IsSharedView;
    public int SelectedValue(string control) => control switch
    {
        "Brightness" => SelectedBrightness, "Contrast" => SelectedContrast, "Saturation" => SelectedSaturation,
        _ => throw new ArgumentException("Unknown control.", nameof(control))
    };
    private string currentText => currentBrightness == SelectedBrightness ? "" : $"Now {IfAvailable(HasBrightness, currentBrightness)}";
    public int CurrentBrightness => currentBrightness;
    public MonitorReading Reading { get; } = reading;
    public MonitorProfile Profile { get; } = profile;
    public void RefreshProfileValues()
    {
        RefreshAdditional();
        foreach (string property in new[] { nameof(DayBrightness), nameof(DayContrast), nameof(DaySaturation),
            nameof(NightBrightness), nameof(NightContrast), nameof(NightSaturation), nameof(SelectedBrightness),
            nameof(SelectedContrast), nameof(SelectedSaturation), nameof(SelectedBrightnessText),
            nameof(SelectedContrastText), nameof(SelectedSaturationText), nameof(CurrentText) })
            OnPropertyChanged(property);
    }
    public string Id => Reading.Id;
    public string Name => Reading.Name;
    public string ShortName => Name.Split(" · ")[0];
    public string DisplayNumber => new string(Reading.Display.Where(char.IsDigit).ToArray());
    public string PositionLabel { get; init; } = "Display";
    public bool HasBrightness => Reading.Brightness.HasValue;
    public bool HasContrast => Reading.Contrast.HasValue;
    public bool HasSaturation => Reading.Saturation.HasValue;
    public string SaturationHint => !HasSaturation ? "Saturation is unavailable for this display."
        : Reading.SaturationSource == "nvidia" ? "50% normal · higher for richer colors"
        : "Higher for richer colors";
    public bool IsPrimary => Reading.IsPrimary;
    public DisplayShape Shape => new(Reading.PixelWidth, Reading.PixelHeight);
    public string ResolutionText => Shape.Resolution;
    public string AspectText => Shape.Aspect;
    public string DisplayDetails => Shape.IsKnown ? $"{Shape.Resolution} · {Shape.Aspect}" : Shape.Resolution;
    public double ScreenHeight => Math.Min(IsPrimary ? 44 : 36, 118 / Shape.Ratio);
    public double ScreenWidth => ScreenHeight * Shape.Ratio;
    public double TrayScreenHeight => Math.Min(24, 70 / Shape.Ratio);
    public double TrayScreenWidth => TrayScreenHeight * Shape.Ratio;
    public string TrayDisplayDescription => $"{Name}\n{DisplayDetails}" + (IsPrimary ? "\nMain display" : "");
    private double cardWidth = 280;
    public double CardWidth { get => cardWidth; set { if (Math.Abs(cardWidth - value) < 0.5) return; cardWidth = value; OnPropertyChanged(); } }
    public bool IsNightProfile
    {
        get => isNightProfile;
        set
        {
            if (isNightProfile == value) return;
            isNightProfile = value;
            RefreshAdditional();
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedProfileName));
            OnPropertyChanged(nameof(SelectedBrightness));
            OnPropertyChanged(nameof(SelectedBrightnessText));
            OnPropertyChanged(nameof(SelectedSaturation));
            OnPropertyChanged(nameof(SelectedSaturationText));
            OnPropertyChanged(nameof(SelectedContrast));
            OnPropertyChanged(nameof(SelectedContrastText));
            OnPropertyChanged(nameof(CurrentText));
        }
    }
    public string SelectedProfileName => IsNightProfile ? "Night" : "Day";
    public int SelectedBrightness
    {
        get => IsNightProfile ? NightBrightness : DayBrightness;
        set
        {
            if (IsNightProfile) NightBrightness = value;
            else DayBrightness = value;
        }
    }
    public string SelectedBrightnessText => HasBrightness ? SelectedBrightness + "%" : "—";
    public string SelectedContrastText => HasContrast ? SelectedContrast.ToString() : "—";
    public string SelectedSaturationText => HasSaturation ? SelectedSaturation.ToString() : "—";
    public int SelectedContrast
    {
        get => IsNightProfile ? NightContrast : DayContrast;
        set
        {
            if (IsNightProfile) NightContrast = value;
            else DayContrast = value;
        }
    }
    public int SelectedSaturation
    {
        get => IsNightProfile ? NightSaturation : DaySaturation;
        set
        {
            if (IsNightProfile) NightSaturation = value;
            else DaySaturation = value;
        }
    }
    public bool CanControl => HasBrightness || HasContrast || HasSaturation || (Reading.Advanced?.Count > 0);
    public bool Enabled { get => Profile.Enabled; set { Profile.Enabled = value; Changed(); } }
    public int DayBrightness { get => Profile.DayBrightness; set => SetControl(value, DayBrightness, v => Profile.DayBrightness = v); }
    public int NightBrightness { get => Profile.NightBrightness; set => SetControl(value, NightBrightness, v => Profile.NightBrightness = v); }
    public int DayContrast { get => Profile.DayContrast; set => SetControl(value, DayContrast, v => Profile.DayContrast = v); }
    public int NightContrast { get => Profile.NightContrast; set => SetControl(value, NightContrast, v => Profile.NightContrast = v); }
    public int DaySaturation { get => Profile.DaySaturation; set => SetControl(value, DaySaturation, v => Profile.DaySaturation = v); }
    public int NightSaturation { get => Profile.NightSaturation; set => SetControl(value, NightSaturation, v => Profile.NightSaturation = v); }
    public bool HasError => status != Reading.Status && !status.StartsWith("Demo", StringComparison.Ordinal);
    public string ErrorText => HasError ? status : "";
    public string Status { get => status; set { if (status == value) return; status = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(ErrorText)); } }
    public string CurrentText => currentText;
    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    public void UpdateCurrent(int? brightness)
    {
        if (!brightness.HasValue || currentBrightness == brightness.Value) return;
        currentBrightness = brightness.Value;
        OnPropertyChanged(nameof(CurrentText));
    }
    private static string IfAvailable(bool supported, int value) => supported ? value + "%" : "—";
    private void SetControl(int value, int previousValue, Action<int> apply, [CallerMemberName] string property = "")
    {
        value = Math.Clamp(value, 0, 100);
        if (value == previousValue) return;
        apply(value);
        Changed(property, previousValue);
        if (property.EndsWith("Brightness", StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedBrightness));
            OnPropertyChanged(nameof(SelectedBrightnessText));
        }
        else if (property.EndsWith("Contrast", StringComparison.Ordinal))
        {
            OnPropertyChanged(nameof(SelectedContrast));
            OnPropertyChanged(nameof(SelectedContrastText));
        }
        else
        {
            OnPropertyChanged(nameof(SelectedSaturation));
            OnPropertyChanged(nameof(SelectedSaturationText));
        }
    }
    private void Changed([CallerMemberName] string? property = null, int? previousValue = null)
    {
        OnPropertyChanged(property);
        OnPropertyChanged(nameof(CurrentText));
        changed(this, property ?? string.Empty, previousValue);
    }
}
