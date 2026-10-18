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
            trayControls.OpenSettings = ShowFromTray;
            trayControls.SelectMonitor = () =>
            {
                if (refreshingTrayControls) return;
                if (trayControls.QuickMonitor.SelectedItem is MonitorViewModel selected) trayMonitorId = selected.Id;
                UpdateTrayControls();
            };
            trayControls.QuickMonitor.ItemsSource = monitors;
            BindQuick(trayControls.QuickMode, ComboBox.SelectedIndexProperty, LinkModeBox, nameof(ComboBox.SelectedIndex), twoWay: true);
            BindQuick(trayControls.QuickSchedule, TextBlock.TextProperty, NextEventText, nameof(TextBlock.Text));
        }
        UpdateTrayControls(force: true);
        trayControls.ShowNearTray();
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
            bool individual = settings.LinkMode == MonitorLinkMode.Individual;
            panel.MonitorSelector.Visibility = individual ? Visibility.Visible : Visibility.Collapsed;
            var selected = monitors.FirstOrDefault(m => m.Id == trayMonitorId)
                ?? monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors.FirstOrDefault();
            panel.QuickMonitor.SelectedItem = selected;
            panel.QuickHint.Text = monitors.Count == 0 ? "No displays found. Open full settings to refresh."
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
            if (trayBoundMode == settings.LinkMode && ReferenceEquals(trayBoundMonitor, selected)) return;
            trayBoundMode = settings.LinkMode;
            trayBoundMonitor = selected;
            foreach (var (name, slider, label, sharedSlider, sharedLabel) in new[]
            {
                ("Brightness", panel.BrightnessSlider, panel.BrightnessValue, SharedBrightnessSlider, SharedBrightnessValue),
                ("Contrast", panel.ContrastSlider, panel.ContrastValue, SharedContrastSlider, SharedContrastValue),
                ("Saturation", panel.SaturationSlider, panel.SaturationValue, SharedSaturationSlider, SharedSaturationValue)
            })
            {
                // Clear the old value binding before changing the range or source. Range coercion must never write to a former display or mode.
                BindingOperations.ClearBinding(slider, Slider.ValueProperty);
                BindingOperations.ClearBinding(slider, Slider.MinimumProperty);
                BindingOperations.ClearBinding(slider, Slider.IsEnabledProperty);
                BindingOperations.ClearBinding(slider, Slider.ToolTipProperty);
                BindingOperations.ClearBinding(label, TextBlock.TextProperty);
                if (individual)
                {
                    slider.Minimum = 0;
                    slider.IsEnabled = selected is not null && SupportsControl(selected, SelectedProperty(name));
                    slider.ToolTip = slider.IsEnabled ? $"Adjust {name.ToLowerInvariant()} for this display." : $"{name} is unavailable on this display.";
                    if (selected is not null)
                    {
                        BindQuick(slider, Slider.ValueProperty, selected, "Selected" + name, twoWay: true);
                        BindQuick(label, TextBlock.TextProperty, selected, "Selected" + name + "Text");
                    }
                    else { slider.Value = 0; label.Text = "Unavailable"; }
                }
                else
                {
                    BindQuick(slider, Slider.MinimumProperty, sharedSlider, nameof(Slider.Minimum));
                    BindQuick(slider, Slider.IsEnabledProperty, sharedSlider, nameof(Slider.IsEnabled));
                    BindQuick(slider, Slider.ToolTipProperty, sharedSlider, nameof(Slider.ToolTip));
                    BindQuick(slider, Slider.ValueProperty, sharedSlider, nameof(Slider.Value), twoWay: true);
                    BindQuick(label, TextBlock.TextProperty, sharedLabel, nameof(TextBlock.Text));
                }
            }
        }
        finally { refreshingTrayControls = false; }
    }
}
