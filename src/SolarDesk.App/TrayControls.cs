using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace SolarDesk;

public partial class MainWindow
{
    private TrayQuickControls? trayControls;
    private string? trayMonitorId;
    private bool refreshingTrayControls;
    private MonitorViewModel? trayBoundMonitor;
    private MonitorLinkMode? trayBoundMode;

    private void ShowTrayControls()
    {
        if (exiting) return;
        if (trayControls is null)
        {
            trayControls = new TrayQuickControls();
            trayControls.Resources.MergedDictionaries.Add(Resources);
            trayControls.SelectProfile = ApplySelectedProfileAsync;
            trayControls.ToggleAutomation = ToggleAutomationAsync;
            trayControls.OpenSettings = ShowFromTray;
            trayControls.RefreshDisplays = () => RefreshTrayDisplaysAsync(force: true);
            trayControls.SelectMode = index =>
            {
                if (!refreshingTrayControls && index is >= 0 and <= 2)
                    LinkModeBox.SetCurrentValue(ComboBox.SelectedIndexProperty, index);
            };
            trayControls.AdjustSharedControl = (name, value) =>
            {
                if (!refreshingTrayControls && trayBoundMode == settings.LinkMode && settings.LinkMode != MonitorLinkMode.Individual)
                    ApplySharedControl(name, value);
            };
            trayControls.SelectMonitor = () =>
            {
                if (refreshingTrayControls) return;
                if (trayControls.QuickMonitor.SelectedItem is MonitorViewModel selected) trayMonitorId = selected.Id;
                UpdateTrayControls();
            };
            trayControls.QuickMonitor.ItemsSource = monitors;
        }
        UpdateTrayControls(force: true);
        trayControls.ShowNearTray();
        _ = RefreshTrayDisplaysAsync(force: false);
    }

    private async Task RefreshTrayDisplaysAsync(bool force)
    {
        if (!ready || exiting || scanningDisplays) return;
        var now = CurrentTime;
        bool needsRefresh = monitors.Count == 0 || monitors.Any(m => !m.HasBrightness || m.HasError)
            || now - lastScan >= TimeSpan.FromMinutes(5);
        if (!force && (!needsRefresh || now - lastTrayRefresh < TimeSpan.FromSeconds(30))) return;
        lastTrayRefresh = now;
        refreshRequested = true;
        await CycleAsync();
    }

    private static void BindQuick(DependencyObject target, DependencyProperty property, object source, string path, bool twoWay = false)
        => BindingOperations.SetBinding(target, property, new Binding(path)
        {
            Source = source, Mode = twoWay ? BindingMode.TwoWay : BindingMode.OneWay,
            UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
        });

    private void UpdateTrayControls(bool force = false)
    {
        if (trayControls is not { } panel || (!panel.IsVisible && !force) || refreshingTrayControls) return;
        refreshingTrayControls = true;
        try
        {
            panel.QuickDay.IsChecked = !selectedNightProfile;
            panel.QuickNight.IsChecked = selectedNightProfile;
            panel.QuickAuto.IsChecked = settings.AutoEnabled;
            panel.QuickAuto.IsEnabled = ready && (settings.AutoEnabled || !settings.HasLocation || monitors.Any(m => m.Enabled && m.CanControl));
            panel.QuickAuto.ToolTip = !settings.HasLocation ? "Set a location to follow sunrise and sunset."
                : !panel.QuickAuto.IsEnabled ? "Enable at least one display with working controls in full settings."
                : "Switch between day and night settings with the sun";
            panel.QuickMode.SelectedIndex = (int)settings.LinkMode;
            panel.QuickSchedule.Text = NextEventText.Text;
            bool individual = settings.LinkMode == MonitorLinkMode.Individual;
            panel.MonitorSelector.Visibility = individual ? Visibility.Visible : Visibility.Collapsed;
            var selected = monitors.FirstOrDefault(m => m.Id == trayMonitorId)
                ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            panel.QuickMonitor.SelectedItem = selected;
            bool detecting = scanningDisplays || refreshRequested;
            panel.QuickRefresh.IsEnabled = !detecting;
            panel.QuickRefresh.Content = detecting ? "Detecting displays…" : "Refresh displays";
            panel.QuickHint.Text = detecting ? "Detecting displays… values will appear when the monitor scan finishes."
                : monitors.Count == 0 ? "No displays found. Check the connection, then choose Refresh displays."
                : individual && selected is { HasBrightness: false } ? "Brightness could not be read. Try Refresh displays and check the monitor's DDC/CI setting."
                : individual ? $"Editing this display's {SelectedProfileName} profile. Changes save automatically."
                : settings.LinkMode == MonitorLinkMode.KeepOffsets ? "All displays · signed offsets. 0 restores the saved baseline."
                : "All displays · moving a slider sets the same value on supported displays.";
            if (saveFailed)
                BindQuick(panel.QuickError, TextBlock.TextProperty, MessageText, nameof(TextBlock.Text));
            else if (individual && selected is not null)
                BindQuick(panel.QuickError, TextBlock.TextProperty, selected, nameof(MonitorViewModel.ErrorText));
            else
            {
                BindingOperations.ClearBinding(panel.QuickError, TextBlock.TextProperty);
                panel.QuickError.Text = string.Join("\n", monitors.Where(m => m.HasError).Select(m => m.Name + ": " + m.ErrorText));
            }
            // Existing bindings carry value changes in both directions without interrupting a drag.
            bool bindValues = force || trayBoundMode != settings.LinkMode || !ReferenceEquals(trayBoundMonitor, selected);
            trayBoundMode = settings.LinkMode;
            trayBoundMonitor = selected;
            var offsets = settings.LinkMode == MonitorLinkMode.KeepOffsets ? EnsureOffsetBaseline() : null;
            foreach (var (name, slider, label) in new[]
            {
                ("Brightness", panel.BrightnessSlider, panel.BrightnessValue),
                ("Contrast", panel.ContrastSlider, panel.ContrastValue),
                ("Saturation", panel.SaturationSlider, panel.SaturationValue)
            })
            {
                // Availability belongs to the monitor, not the disabled/hidden main-window surface.
                BindingOperations.ClearBinding(slider, Slider.IsEnabledProperty);
                slider.IsEnabled = individual ? selected is not null && SupportsControl(selected, SelectedProperty(name))
                    : monitors.Any(m => SupportsControl(m, SelectedProperty(name)));
                if (!bindValues && individual)
                {
                    if (individual && selected is null) label.Text = detecting ? "Detecting…" : "Unavailable";
                    continue;
                }
                // Clear the old value binding before changing the range or source. Range coercion must never write to a former display or mode.
                BindingOperations.ClearBinding(slider, Slider.ValueProperty);
                BindingOperations.ClearBinding(slider, Slider.MinimumProperty);
                BindingOperations.ClearBinding(slider, Slider.ToolTipProperty);
                BindingOperations.ClearBinding(label, TextBlock.TextProperty);
                if (individual)
                {
                    slider.Minimum = 0;
                    slider.ToolTip = slider.IsEnabled ? $"Adjust {name.ToLowerInvariant()} for this display." : $"{name} is unavailable on this display.";
                    if (selected is not null)
                    {
                        BindQuick(slider, Slider.ValueProperty, selected, "Selected" + name, twoWay: true);
                        BindQuick(label, TextBlock.TextProperty, selected, "Selected" + name + "Text");
                    }
                    else { slider.Value = 0; label.Text = detecting ? "Detecting…" : "Unavailable"; }
                }
                else
                {
                    // Read saved control state directly. The full window may never have been shown.
                    var supported = monitors.Where(m => SupportsControl(m, SelectedProperty(name))).ToList();
                    int value = offsets?.Offsets.Get(name)
                        ?? (supported.FirstOrDefault(m => m.IsPrimary) ?? supported.FirstOrDefault())?.SelectedValue(name) ?? 0;
                    bool mixed = offsets is null && supported.Select(m => m.SelectedValue(name)).Distinct().Skip(1).Any();
                    slider.Minimum = offsets is null ? 0 : -100;
                    slider.Value = value;
                    label.Text = supported.Count == 0 ? (detecting ? "Detecting…" : "Unavailable")
                        : mixed ? "Mixed" : offsets is not null ? value.ToString("+0;-0;0") : value + "%";
                    slider.ToolTip = supported.Count == 0 ? $"{name} could not be read. Try Refresh displays."
                        : offsets is not null ? $"Offset from the saved {SelectedProfileName} baseline. 0 restores it."
                        : $"Set {name.ToLowerInvariant()} on {supported.Count} displays.";
                }
            }
        }
        finally { refreshingTrayControls = false; }
    }
}
