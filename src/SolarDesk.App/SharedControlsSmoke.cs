using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Text.Json;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckLinkedControlsAsync(Action<bool, string> check, string previewDirectory)
    {
        if (!diagnostic) throw new InvalidOperationException("Shared control checks require demo mode.");
        DayTab.IsChecked = true;
        string[] properties = ["DayBrightness", "NightBrightness", "DayContrast", "NightContrast", "DaySaturation", "NightSaturation"];
        int[][] original = monitors.Select(m => properties.Select(p => ReadControlValue(m, p)).ToArray()).ToArray();
        void Edit(Slider slider, double value) { slider.SetCurrentValue(Slider.ValueProperty, value); UpdateLayout(); }
        bool Brightness(params int[] values) => monitors.Select(m => m.SelectedBrightness).SequenceEqual(values);
        UpdateLayout();
        var individualSliders = VisualChildren<Slider>(MonitorItems).Where(s => s.Orientation == Orientation.Vertical).ToList();
        LinkModeBox.SelectedIndex = 1;
        UpdateLayout();
        check(pendingSliderEdits.Count == 0 && monitors.Select(m => m.DayBrightness).SequenceEqual(original.Select(v => v[0])),
            "Selecting Mass preserves every value until a shared slider moves");
        check(monitors.All(m => m.IsSharedView) && individualSliders.All(s => !s.IsVisible)
            && VisualChildren<Slider>(SharedControlsPanel).Count(s => s.IsVisible && s.Orientation == Orientation.Vertical) == 3
            && !MonitorScroll.IsVisible && SharedMonitorScreen.IsVisible,
            "Mass shows one monitor panel with three vertical controls for all displays");
        check(SharedBrightnessValue.Text == "Mixed" && MatchMainButton.IsVisible, "Mixed values are explicit and offer Match main");
        await RenderAsync(Path.Combine(previewDirectory, "mass-mixed.png"));
        var primary = monitors.First(m => m.IsPrimary);
        int mainBrightness = primary.DayBrightness, mainContrast = primary.DayContrast, mainSaturation = primary.DaySaturation;
        MatchMainClick(this, new RoutedEventArgs());
        check(monitors.All(m => m.DayBrightness == mainBrightness && m.DayContrast == mainContrast && m.DaySaturation == mainSaturation)
            && MatchMainButton.Visibility == Visibility.Collapsed, "Match main copies all three controls without moving its slider first");
        Edit(SharedBrightnessSlider, 68); Edit(SharedContrastSlider, 47); Edit(SharedSaturationSlider, 66);
        await FlushSliderEditsAsync();
        check(monitors.All(m => m.DayBrightness == 68 && m.CurrentBrightness == 68 && m.DayContrast == 47 && m.DaySaturation == 66)
            && SharedBrightnessValue.Text == "68%", "Three shared sliders update every supported monitor and value label");
        check(monitors.Select(m => m.NightBrightness).SequenceEqual(original.Select(v => v[1]))
            && monitors.Select(m => m.NightContrast).SequenceEqual(original.Select(v => v[3]))
            && monitors.Select(m => m.NightSaturation).SequenceEqual(original.Select(v => v[5])), "Shared Day edits preserve Night settings");
        Edit(SharedSaturationSlider, 0);
        check(ColorSpread(SaturationThumbColor(SharedSaturationSlider)) == 0, "Combined saturation turns grey at zero");
        Edit(SharedSaturationSlider, 100);
        check(ColorSpread(SaturationThumbColor(SharedSaturationSlider)) > 80, "Combined saturation gains color at 100");
        Edit(SharedSaturationSlider, 66);
        await FlushSliderEditsAsync();
        await RenderAsync(Path.Combine(previewDirectory, "mass-mode.png"));
        LinkModeBox.SelectedIndex = 0;
        check(monitors.Select(m => properties.Select(p => ReadControlValue(m, p)).ToArray()).Select((values, i) => values.SequenceEqual(original[i])).All(same => same),
            "Returning to Individual restores its original brightness, contrast and saturation for Day and Night");
        for (int i = 0; i < monitors.Count; i++)
        {
            monitors[i].DayBrightness = 30 + i * 20;
            monitors[i].DayContrast = 40 + i * 10;
            monitors[i].DaySaturation = 35 + i * 15;
        }
        await FlushSliderEditsAsync();
        LinkModeBox.SelectedIndex = 2;
        check(Brightness(30, 50, 70) && SharedBrightnessSlider.Value == 0 && SharedContrastSlider.Value == 0
            && SharedSaturationSlider.Value == 0 && !ResetOffsetsButton.IsEnabled && pendingSliderEdits.Count == 0,
            "Offset captures existing profile values as zero without applying anything");
        check(!MonitorScroll.IsVisible && SharedMonitorScreen.IsVisible
            && VisualChildren<Slider>(SharedControlsPanel).Where(s => s.IsVisible).All(s => s.Orientation == Orientation.Vertical),
            "Offset uses the same single monitor panel and vertical controls");
        check(SaturationAppearance.GetLevel(SharedSaturationSlider) == 50 && ColorSpread(SaturationThumbColor(SharedSaturationSlider)) > 0,
            "Zero saturation offset retains the baseline's color intensity instead of turning grey");
        await RenderAsync(Path.Combine(previewDirectory, "offset-zero.png"));
        Edit(SharedBrightnessSlider, 10); Edit(SharedContrastSlider, 5); Edit(SharedSaturationSlider, 10);
        check(Brightness(40, 60, 80) && monitors.Select(m => m.DayContrast).SequenceEqual([45, 55, 65])
            && monitors.Select(m => m.DaySaturation).SequenceEqual([45, 60, 75]) && SharedBrightnessValue.Text == "+10",
            "Offsets add signed percentage points to each monitor's baseline");
        check(SaturationAppearance.GetLevel(SharedSaturationSlider) == 60,
            "Saturation offset color follows the group's resulting average value");
        await RenderAsync(Path.Combine(previewDirectory, "offset-mode.png"));
        Edit(SharedBrightnessSlider, 20);
        check(Brightness(50, 70, 90), "Repeated offset changes recompute from the baseline instead of compounding");
        Edit(SharedBrightnessSlider, 100);
        check(Brightness(60, 80, 100) && SharedBrightnessSlider.Value == 30 && SharedBrightnessValue.Text == "+30",
            "The whole group and shared thumb stop at the upper limit");
        Edit(SharedBrightnessSlider, 20);
        check(Brightness(50, 70, 90), "Offsets reverse immediately after reaching a limit");
        Edit(SharedBrightnessSlider, -100);
        check(Brightness(0, 20, 40) && SharedBrightnessSlider.Value == -30, "Lower limits preserve differences and update the shared thumb");
        Slider.IncreaseSmall.Execute(null, SharedBrightnessSlider);
        check(Brightness(1, 21, 41), "Keyboard adjustment works on shared signed sliders");
        ResetOffsetsClick(this, new RoutedEventArgs());
        check(Brightness(30, 50, 70) && monitors.Select(m => m.DayContrast).SequenceEqual([40, 50, 60])
            && monitors.Select(m => m.DaySaturation).SequenceEqual([35, 50, 65]) && !ResetOffsetsButton.IsEnabled,
            "Reset to zero restores all three saved baseline controls");
        Edit(SharedBrightnessSlider, 10);
        await FlushSliderEditsAsync();
        NightTab.IsChecked = true;
        check(SharedBrightnessSlider.Value == 0, "Night begins with its own zero baseline");
        Edit(SharedBrightnessSlider, 5);
        await FlushSliderEditsAsync();
        check(monitors.Select(m => m.NightBrightness).SequenceEqual(original.Select(v => v[1] + 5))
            && monitors.Select(m => m.DayBrightness).SequenceEqual([40, 60, 80]), "Night offsets leave Day values untouched");
        await Task.Delay(1000);
        await RenderAsync(Path.Combine(previewDirectory, "shared-night.png"));
        DayTab.IsChecked = true;
        check(SharedBrightnessSlider.Value == 10 && Brightness(40, 60, 80), "Switching profiles restores the selected profile's offset");
        var persisted = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings))!;
        settings.DayOffsets = persisted.DayOffsets;
        settings.NightOffsets = persisted.NightOffsets;
        UpdateSharedControls();
        check(SharedBrightnessSlider.Value == 10 && settings.DayOffsets!.Baselines[monitors[0].Id].Brightness == 30,
            "Saved offsets and their original baselines survive a settings round trip");
        LinkModeBox.SelectedIndex = 1;
        await FlushSliderEditsAsync();
        check(Brightness(68, 68, 68) && monitors.All(m => m.CurrentBrightness == 68 && m.DayContrast == 47 && m.DaySaturation == 66),
            "Returning to Mass restores and applies its own three saved values");
        NightTab.IsChecked = true;
        Edit(SharedBrightnessSlider, 31); Edit(SharedContrastSlider, 62); Edit(SharedSaturationSlider, 44);
        await FlushSliderEditsAsync();
        DayTab.IsChecked = true;
        LinkModeBox.SelectedIndex = 0;
        await FlushSliderEditsAsync();
        check(Brightness(30, 50, 70) && monitors.Select(m => m.NightBrightness).SequenceEqual(original.Select(v => v[1]))
            && monitors.Select(m => m.NightContrast).SequenceEqual(original.Select(v => v[3]))
            && monitors.Select(m => m.NightSaturation).SequenceEqual(original.Select(v => v[5])),
            "Combined Night edits cannot overwrite either Individual profile");
        LinkModeBox.SelectedIndex = 1;
        Edit(SharedBrightnessSlider, 74);
        LinkModeBox.SelectedIndex = 0;
        await FlushSliderEditsAsync();
        check(Brightness(30, 50, 70) && monitors.Select(m => m.CurrentBrightness).SequenceEqual([30, 50, 70]),
            "Switching before the edit timer fires applies Individual values instead of stale Combined edits");
        LinkModeBox.SelectedIndex = 1;
        await FlushSliderEditsAsync();
        check(Brightness(74, 74, 74) && monitors.All(m => m.CurrentBrightness == 74 && m.NightBrightness == 31
            && m.NightContrast == 62 && m.NightSaturation == 44), "Rapid switches still save the final Combined edit and both profiles");
        LinkModeBox.SelectedIndex = 2;
        await FlushSliderEditsAsync();
        check(Brightness(40, 60, 80) && SharedBrightnessSlider.Value == 10 && settings.DayOffsets!.Baselines[monitors[0].Id].Brightness == 30,
            "Offset restores its values, baseline and signed adjustment after edits in other modes");
        NightTab.IsChecked = true;
        check(SharedBrightnessSlider.Value == 5 && monitors.Select(m => m.NightBrightness).SequenceEqual(original.Select(v => v[1] + 5)),
            "Offset keeps its Night baseline and adjustment independently of Combined Night values");
        DayTab.IsChecked = true;
        double width = Width, height = Height;
        Width = MinWidth; Height = MinHeight;
        await Task.Delay(1000);
        await RenderAsync(Path.Combine(previewDirectory, "shared-small.png"));
        check(SharedControlsPanel.ActualWidth <= 450 && SharedBrightnessSlider.ActualWidth >= 44
            && SharedSaturationSlider.TranslatePoint(new Point(SharedSaturationSlider.ActualWidth, 0), this).X < ActualWidth
            && SharedSaturationSlider.TranslatePoint(new Point(0, SharedSaturationSlider.ActualHeight), this).Y <= ActualHeight - 42
            && !MonitorScroll.IsVisible,
            "All shared controls fit at minimum window size");
        Width = width; Height = height;
        var unsupported = new MonitorViewModel(DemoReadings()[0] with { Id = "unsupported-demo", Brightness = null, Saturation = null },
            new MonitorProfile(), ProfileChanged);
        settings.Monitors[unsupported.Id] = unsupported.Profile;
        monitors.Add(unsupported); UpdateLinkedControls();
        check(SharedBrightnessSlider.Value == 0 && monitors.Take(3).Select(m => m.DayBrightness).SequenceEqual([40, 60, 80]),
            "Changing the monitor group rebases zero without moving displays");
        LinkModeBox.SelectedIndex = 1;
        await FlushSliderEditsAsync();
        Edit(SharedBrightnessSlider, 73); Edit(SharedSaturationSlider, 64);
        check(unsupported.DayBrightness == 65 && unsupported.DaySaturation == 50 && !pendingSliderEdits.ContainsKey(unsupported.Id),
            "Shared controls skip unsupported monitor capabilities");
        monitors.Remove(unsupported);
        await FlushSliderEditsAsync();
        LinkModeBox.SelectedIndex = 0; UpdateLayout();
        var individual = individualSliders.First(s => s.DataContext == monitors[0] && AutomationProperties.GetName(s) == "Brightness");
        Edit(individual, 72);
        check(monitors[1].DayBrightness == 50 && monitors[2].DayBrightness == 70
            && individualSliders.All(s => s.IsVisible) && !SharedControlsPanel.IsVisible,
            "Individual restores per-monitor sliders and only edits the selected display");
        LinkModeBox.SelectedIndex = 2;
        check(SharedBrightnessSlider.Value == 0 && Brightness(40, 60, 80), "Individual edits leave saved Offset values intact after a monitor-group change");
        LinkModeBox.SelectedIndex = 0;
        for (int i = 0; i < monitors.Count; i++)
            for (int p = 0; p < properties.Length; p++) WriteControlValue(monitors[i], properties[p], original[i][p]);
        await FlushSliderEditsAsync(); await RestoreAsync();
        LinkModeBox.IsDropDownOpen = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var popup = (System.Windows.Controls.Primitives.Popup)LinkModeBox.Template.FindName("PART_Popup", LinkModeBox);
        check(popup.IsOpen && popup.Child is FrameworkElement { ActualHeight: > 0 }, "Mode selector opens a visible menu with all three options");
        LinkModeBox.IsDropDownOpen = false;
        await Task.Delay(1200);
    }
}
