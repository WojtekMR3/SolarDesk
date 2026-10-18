using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SolarDesk;

public partial class MainWindow
{
    private bool applyingSharedValues, refreshingSharedPanel;
    private static readonly string[] ControlNames = ["Brightness", "Contrast", "Saturation"];

    private string ControlHint => settings.LinkMode switch
    {
        MonitorLinkMode.SameValues => "Mass mode · shared values are saved separately from Individual and Offset.",
        MonitorLinkMode.KeepOffsets => $"Offset mode · saved separately · 0 restores its {SelectedProfileName} baseline.",
        _ => "Individual values are saved separately from Mass and Offset."
    };

    private void LinkModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ready || LinkModeBox.SelectedIndex < 0) return;
        var next = (MonitorLinkMode)LinkModeBox.SelectedIndex;
        if (next == settings.LinkMode) return;
        bool restoring = settings.ModePresets.TryGetValue(next, out var preset) && preset.Monitors.Count > 0;
        var before = monitors.ToDictionary(m => m.Id, m => new ControlValues(m.SelectedBrightness, m.SelectedContrast, m.SelectedSaturation));
        bool hadPendingEdits = pendingSliderEdits.Count > 0;
        CancelQueuedPreview();
        settings.SelectMode(next);
        foreach (var monitor in monitors) monitor.RefreshProfileValues();
        UpdateLinkedControls();
        // Replace unsent edits with the newly selected mode. Rapid switches always apply the latest selection.
        // Previously queued hardware writes finish before these writes on the monitor service's serial worker.
        bool changed = monitors.Any(m => ControlNames.Any(name => SupportsControl(m, SelectedProperty(name))
            && before[m.Id].Get(name) != m.SelectedValue(name)));
        if (restoring || hadPendingEdits || changed)
            QueueSelectedProfile();
        if (!SaveSettings()) { pendingSliderEdits.Clear(); saveTimer.Stop(); return; }
        Message(ControlHint);
    }

    private void UpdateLinkedControls()
    {
        LinkModeBox.ToolTip = ControlHint;
        LinkedModeBadge.Visibility = settings.LinkMode == MonitorLinkMode.Individual ? Visibility.Collapsed : Visibility.Visible;
        LinkedModeBadge.ToolTip = ControlHint;
        bool shared = settings.LinkMode != MonitorLinkMode.Individual;
        foreach (var monitor in monitors) monitor.IsSharedView = shared;
        MonitorScroll.Visibility = shared ? Visibility.Collapsed : Visibility.Visible;
        SharedControlsPanel.Visibility = shared ? Visibility.Visible : Visibility.Collapsed;
        UpdateSharedControls();
    }

    private OffsetProfile? SelectedOffsets
    {
        get => selectedNightProfile ? settings.NightOffsets : settings.DayOffsets;
        set { if (selectedNightProfile) settings.NightOffsets = value; else settings.DayOffsets = value; }
    }

    private string SelectedProperty(string control) => SelectedProfileName + control;

    private OffsetProfile EnsureOffsetBaseline()
    {
        var state = SelectedOffsets;
        bool matches = state is not null && state.IsValid() && state.Baselines.Keys.ToHashSet().SetEquals(monitors.Select(m => m.Id))
            && monitors.All(m => ControlNames.Where(name => SupportsControl(m, SelectedProperty(name))).All(name =>
                ReadControlValue(m, SelectedProperty(name)) == state.Baselines[m.Id].Get(name) + state.Offsets.Get(name)));
        if (!matches)
        {
            // New/removed displays or independent edits establish a new zero without changing any values.
            state = new OffsetProfile
            {
                Baselines = monitors.ToDictionary(m => m.Id, m => new ControlValues(m.SelectedBrightness, m.SelectedContrast, m.SelectedSaturation))
            };
            SelectedOffsets = state;
        }
        return state!;
    }

    private void SharedSliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready || refreshingSharedPanel || settings.LinkMode == MonitorLinkMode.Individual || sender is not Slider { Tag: string name }) return;
        ApplySharedControl(name, (int)Math.Round(e.NewValue));
    }

    private void ApplySharedControl(string name, int requested)
    {
        string property = SelectedProperty(name);
        var supported = monitors.Where(m => SupportsControl(m, property)).ToList();
        if (supported.Count == 0) return;
        int[] values;
        if (settings.LinkMode == MonitorLinkMode.KeepOffsets)
        {
            var state = EnsureOffsetBaseline();
            var baselines = supported.Select(m => state.Baselines[m.Id].Get(name)).ToArray();
            var applied = LinkedControls.ApplyOffset(baselines, requested);
            state.SetOffset(name, applied.Offset);
            values = applied.Values;
        }
        else
        {
            values = Enumerable.Repeat(Math.Clamp(requested, 0, 100), supported.Count).ToArray();
        }
        applyingSharedValues = true;
        try { for (int i = 0; i < supported.Count; i++) WriteControlValue(supported[i], property, values[i]); }
        finally { applyingSharedValues = false; }
        ScheduleSave();
        UpdateSharedControls();
    }

    private void UpdateSharedControls()
    {
        if (!ready || refreshingSharedPanel || SharedBrightnessSlider is null) return;
        refreshingSharedPanel = true;
        try
        {
            bool offset = settings.LinkMode == MonitorLinkMode.KeepOffsets;
            var state = offset ? EnsureOffsetBaseline() : null;
            SharedControlsTitle.Text = $"All {monitors.Count} displays";
            SharedDisplayList.Text = "Displays " + string.Join(" · ", monitors.Select(m => m.DisplayNumber));
            SharedControlsHint.Text = offset
                ? $"0 = saved {SelectedProfileName} values · differences stay the same"
                : "Shared values saved for Mass mode.";
            ResetOffsetsButton.Visibility = offset ? Visibility.Visible : Visibility.Collapsed;
            ResetOffsetsButton.IsEnabled = state?.Offsets != new ControlValues(0, 0, 0);
            foreach (var (name, slider, label, range) in new[] {
                ("Brightness", SharedBrightnessSlider, SharedBrightnessValue, SharedBrightnessRange),
                ("Contrast", SharedContrastSlider, SharedContrastValue, SharedContrastRange),
                ("Saturation", SharedSaturationSlider, SharedSaturationValue, SharedSaturationRange) })
            {
                string property = SelectedProperty(name);
                var supported = monitors.Where(m => SupportsControl(m, property)).ToList();
                slider.IsEnabled = supported.Count > 0;
                slider.Minimum = offset ? -100 : 0;
                slider.Maximum = 100;
                slider.Style = (Style)FindResource(offset ? "VerticalOffsetControl" : "VerticalControl");
                range.Visibility = offset ? Visibility.Visible : Visibility.Collapsed;
                int value = state?.Offsets.Get(name) ?? (supported.FirstOrDefault(m => m.IsPrimary) ?? supported.FirstOrDefault())?.SelectedValue(name) ?? 0;
                slider.Value = value;
                // Zero offset keeps the baseline's color intensity; it does not mean zero saturation.
                if (name == "Saturation")
                    SaturationAppearance.SetLevel(slider, offset && supported.Count > 0
                        ? supported.Average(m => m.SelectedSaturation) : value);
                bool mixed = !offset && supported.Select(m => ReadControlValue(m, property)).Distinct().Skip(1).Any();
                label.Text = supported.Count == 0 ? "Unavailable" : mixed ? "Mixed" : offset ? value.ToString("+0;-0;0") : value + "%";
                slider.ToolTip = supported.Count == 0 ? $"{name} is unavailable on these displays."
                    : offset ? $"Offset in percentage points from the saved {SelectedProfileName} baseline. 0 restores it."
                    : mixed ? $"Different values · slider starts at the main display's value ({value}%). Drag to set all displays."
                    : $"Set {name.ToLowerInvariant()} on {supported.Count} displays.";
            }
        }
        finally { refreshingSharedPanel = false; }
        UpdateMatchMainButton();
    }

    private void ResetOffsetsClick(object sender, RoutedEventArgs e)
    {
        if (settings.LinkMode != MonitorLinkMode.KeepOffsets) return;
        foreach (string name in ControlNames) ApplySharedControl(name, 0);
    }

    private void UpdateMatchMainButton()
    {
        bool mixed = settings.LinkMode == MonitorLinkMode.SameValues && ControlNames.Any(name =>
        {
            string property = SelectedProfileName + name;
            return monitors.Where(m => SupportsControl(m, property)).Select(m => ReadControlValue(m, property)).Distinct().Skip(1).Any();
        });
        MatchMainButton.Visibility = mixed ? Visibility.Visible : Visibility.Collapsed;
    }

    private void MatchMainClick(object sender, RoutedEventArgs e)
    {
        if (settings.LinkMode != MonitorLinkMode.SameValues) return;
        foreach (string name in ControlNames)
        {
            string property = SelectedProfileName + name;
            var supported = monitors.Where(m => SupportsControl(m, property)).ToList();
            var source = supported.FirstOrDefault(m => m.IsPrimary) ?? supported.FirstOrDefault();
            if (source is not null) ApplySharedControl(name, ReadControlValue(source, property));
        }
    }

    private static bool SupportsControl(MonitorViewModel monitor, string property) => property switch
    {
        nameof(MonitorViewModel.DayBrightness) or nameof(MonitorViewModel.NightBrightness) => monitor.HasBrightness,
        nameof(MonitorViewModel.DayContrast) or nameof(MonitorViewModel.NightContrast) => monitor.HasContrast,
        nameof(MonitorViewModel.DaySaturation) or nameof(MonitorViewModel.NightSaturation) => monitor.HasSaturation,
        _ => false
    };

    private static int ReadControlValue(MonitorViewModel monitor, string property) => property switch
    {
        nameof(MonitorViewModel.DayBrightness) => monitor.DayBrightness,
        nameof(MonitorViewModel.NightBrightness) => monitor.NightBrightness,
        nameof(MonitorViewModel.DayContrast) => monitor.DayContrast,
        nameof(MonitorViewModel.NightContrast) => monitor.NightContrast,
        nameof(MonitorViewModel.DaySaturation) => monitor.DaySaturation,
        nameof(MonitorViewModel.NightSaturation) => monitor.NightSaturation,
        _ => throw new ArgumentException("Unknown monitor control.", nameof(property))
    };

    private static void WriteControlValue(MonitorViewModel monitor, string property, int value)
    {
        switch (property)
        {
            case nameof(MonitorViewModel.DayBrightness): monitor.DayBrightness = value; break;
            case nameof(MonitorViewModel.NightBrightness): monitor.NightBrightness = value; break;
            case nameof(MonitorViewModel.DayContrast): monitor.DayContrast = value; break;
            case nameof(MonitorViewModel.NightContrast): monitor.NightContrast = value; break;
            case nameof(MonitorViewModel.DaySaturation): monitor.DaySaturation = value; break;
            case nameof(MonitorViewModel.NightSaturation): monitor.NightSaturation = value; break;
        }
    }

}
