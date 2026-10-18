using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;

namespace SolarDesk;

public partial class MainWindow
{
    private static List<AdvancedControl> DemoAdvancedControls() =>
    [
        new(AdvancedKind.Sharpness, "Sharpness", 5, 0, 10, []),
        new(AdvancedKind.Temperature, "Warmth", 6500, 3000, 30000, [new(4000, "4000 K"), new(5000, "5000 K"), new(6500, "6500 K"), new(7500, "7500 K")]),
        new(AdvancedKind.Red, "Red balance", 50, 0, 100, []),
        new(AdvancedKind.Green, "Green balance", 50, 0, 100, []),
        new(AdvancedKind.Blue, "Blue balance", 50, 0, 100, []),
        new(AdvancedKind.Gamma, "Gamma", 0x7800, 0, 65535, DdcCapabilities.GammaChoices([0, 0x78, 0xfb, 0x50, 0x64, 0x78, 0x8c]))
    ];

    private async Task CheckAdditionalSettingsAsync(Action<bool, string> check, string directory)
    {
        if (!diagnostic) throw new InvalidOperationException("Additional settings checks require demo mode.");
        bool handled = false;
        var wideParameter = new IntPtr(0x1_0000_0000L);
        PresetWindowMessage(IntPtr.Zero, 0x020a, wideParameter, IntPtr.Zero, ref handled);
        PresetWindowMessage(IntPtr.Zero, 0x0312, wideParameter, IntPtr.Zero, ref handled);
        check(!handled, "Preset shortcuts ignore unrelated 64-bit window messages and unknown hotkey IDs");
        LinkModeBox.SelectedIndex = 0; DayTab.IsChecked = true;
        await FlushSliderEditsAsync(); UpdateLayout();
        var main = monitors.First(m => m.IsPrimary);
        var original = DisplayProfileValues.Capture(main.Profile);
        var expander = VisualChildren<Expander>(MonitorItems).First(e => e.DataContext == main && AutomationProperties.GetName(e) == "Additional monitor settings");
        check(!expander.IsExpanded && !main.Profile.AdvancedManaged && main.AdvancedControls.Count == 6,
            "Additional settings start collapsed with capability-specific controls and no automatic hardware edits");
        expander.IsExpanded = true; UpdateLayout();
        check(DisplayProfileValues.Capture(main.Profile) == original && !main.Profile.AdvancedManaged && pendingSliderEdits.Count == 0,
            "Opening Additional settings never changes a monitor value");
        var warmthBox = VisualChildren<ComboBox>(expander).First(c => AutomationProperties.GetName(c) == "Warmth");
        warmthBox.IsDropDownOpen = true;
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        check(warmthBox.Items.Count == 4 && ((System.Windows.Controls.Primitives.Popup)warmthBox.Template.FindName("PART_Popup", warmthBox)).IsOpen,
            "Additional dropdowns open and show the display's supported choices");
        warmthBox.IsDropDownOpen = false;
        check(VisualChildren<TextBlock>(warmthBox).Any(t => t.Text == "6500 K"), "Additional dropdowns show readable labels rather than internal objects");
        var sharpness = main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Sharpness);
        sharpness.Value = 7;
        check(main.Profile.DayAdvanced.Sharpness == 7 && main.Profile.NightAdvanced.Sharpness == original.NightAdvanced!.Sharpness,
            "Sharpness edits only the selected profile");
        main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Temperature).Choice = 5000;
        main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Red).Value = 44;
        check(main.Profile.DayAdvanced.Temperature is null && main.Profile.DayAdvanced.Red == 44
            && main.Profile.DayAdvanced.Green == 50 && main.Profile.DayAdvanced.Blue == 50,
            "Custom RGB replaces the warmth preset");
        main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Temperature).Choice = 5000;
        check(main.Profile.DayAdvanced.Temperature == 5000 && main.Profile.DayAdvanced.Red is null,
            "Warmth replaces custom RGB without conflicting writes");
        main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Gamma).Choice = 0x6400;
        await FlushSliderEditsAsync();
        main.PresetName = "Reading"; main.PresetHotkey = 1;
        check(SaveMonitorPreset(main) && main.Profile.Presets.Single().Advanced.Gamma == 0x6400,
            "Named presets save the selected profile's hardware controls and shortcut");
        var other = monitors.First(m => !m.IsPrimary);
        other.PresetName = "Conflict"; other.PresetHotkey = 1;
        check(!SaveMonitorPreset(other), "A shortcut cannot silently replace another display's preset");
        sharpness.Value = 2;
        await ApplyPresetHotkeyAsync(1);
        check(main.Profile.DayAdvanced.Sharpness == 7, "Preset shortcut dispatch restores the saved values");
        LinkModeBox.SelectedIndex = 1; sharpness.Value = 3;
        await FlushSliderEditsAsync(); LinkModeBox.SelectedIndex = 0;
        check(main.Profile.DayAdvanced.Sharpness == 7, "Additional settings are remembered independently for each mode");
        main.SelectedBrightness = 100;
        bool maximumReached = main.SelectedBrightness == 100;
        main.SelectedBrightness = 0;
        check(maximumReached && main.SelectedBrightness == 0, "Individual brightness uses the full 0 to 100 range");
        LinkModeBox.SelectedIndex = 1; SharedBrightnessSlider.Value = 100;
        check(monitors.All(m => m.DayBrightness == 100), "Combined brightness can reach 100 on every supported display");
        SharedBrightnessSlider.Value = 0;
        check(monitors.All(m => m.DayBrightness == 0), "Combined brightness can reach 0 on every supported display");
        LinkModeBox.SelectedIndex = 2;
        var offsetBaselines = monitors.Select(m => m.DayBrightness).ToArray();
        SharedBrightnessSlider.Value = 100;
        var offsetValues = monitors.Select(m => m.DayBrightness).ToArray();
        check(offsetValues.Max() == 100 && offsetValues.Zip(offsetBaselines, (value, baseline) => value - baseline).Distinct().Count() == 1,
            "Offset brightness reaches 100 while preserving display differences");
        SharedBrightnessSlider.Value = -100;
        check(monitors.Min(m => m.DayBrightness) == 0, "Offset brightness can reach the lower hardware endpoint");
        LinkModeBox.SelectedIndex = 0;
        original.Apply(main.Profile); main.RefreshProfileValues();
        main.Profile.AdvancedManaged = false;
        await FlushSliderEditsAsync();
        await Task.Delay(1200);
        var scroll = VisualChildren<ScrollViewer>(MainSurface).First(s => s != MonitorScroll && s.VerticalScrollBarVisibility == ScrollBarVisibility.Auto);
        double width = Width, height = Height;
        Height = 1480; UpdateLayout(); scroll.ScrollToTop();
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        await RenderAsync(Path.Combine(directory, "additional-settings-day.png"));
        NightTab.IsChecked = true; await Task.Delay(1200);
        await RenderAsync(Path.Combine(directory, "additional-settings-night.png"));
        DayTab.IsChecked = true;
        Width = MinWidth; Height = MinHeight; UpdateLayout();
        var save = VisualChildren<Button>(expander).First(b => Equals(b.Content, "Save current as preset"));
        save.BringIntoView(); await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        check(save.IsVisible && save.TranslatePoint(new Point(0, save.ActualHeight), this).Y < ActualHeight - 30,
            "Expanded additional settings and preset actions remain reachable at minimum window size");
        await RenderAsync(Path.Combine(directory, "additional-settings-small.png"));
        Width = width; Height = height; expander.IsExpanded = false; scroll.ScrollToTop();
        main.Profile.Presets.Clear(); main.RefreshPresets(); main.PresetName = ""; main.PresetHotkey = 0;
        other.PresetName = ""; other.PresetHotkey = 0;
        LinkModeBox.SelectedIndex = 1; UpdateLayout();
        var shared = VisualChildren<Expander>(SharedControlsPanel).First(e => AutomationProperties.GetName(e) == "Additional settings for all displays");
        shared.IsExpanded = true; UpdateLayout();
        check(SharedAdditionalItems.Items.Count == monitors.Count && main.IsSharedView, "Combined and Offset expose per-display additional settings beneath the shared controls");
        shared.IsExpanded = false; LinkModeBox.SelectedIndex = 0;
        await FlushSliderEditsAsync(); await RestoreAsync(); await Task.Delay(1200);
    }
}
