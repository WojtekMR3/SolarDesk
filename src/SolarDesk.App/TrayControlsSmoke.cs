using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckTrayControlsAsync(Action<bool, string> check, string previewDirectory)
    {
        if (!diagnostic) throw new InvalidOperationException("Tray checks require demo mode.");
        settings.AutoEnabled = false;
        LinkModeBox.SelectedIndex = 0;
        await ApplySelectedProfileAsync(false);
        await FlushSliderEditsAsync();
        int[] before = monitors.Select(m => m.DayBrightness).ToArray();
        Hide();
        ShowTrayControls();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var panel = trayControls!;
        check(panel.IsVisible && !IsVisible && panel.QuickMonitor.SelectedItem == monitors.First(m => m.IsPrimary)
            && pendingSliderEdits.Count == 0 && monitors.Select(m => m.DayBrightness).SequenceEqual(before),
            "Tray quick controls open independently, select the main display and do not change settings");

        panel.QuickMonitor.SelectedItem = monitors[0];
        int nightBrightness = monitors[0].NightBrightness;
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, 37.0);
        await FlushSliderEditsAsync();
        check(monitors[0].DayBrightness == 37 && monitors[0].CurrentBrightness == 37
            && monitors[0].NightBrightness == nightBrightness
            && monitors.Skip(1).Select(m => m.DayBrightness).SequenceEqual(before.Skip(1)),
            "Tray brightness applies and saves only the selected display and Day profile");
        panel.ContrastSlider.SetCurrentValue(Slider.ValueProperty, 46.0);
        panel.SaturationSlider.SetCurrentValue(Slider.ValueProperty, 63.0);
        Slider.IncreaseSmall.Execute(null, panel.BrightnessSlider);
        await FlushSliderEditsAsync();
        check(monitors[0].DayBrightness == 38 && monitors[0].DayContrast == 46 && monitors[0].DaySaturation == 63,
            "Tray contrast, saturation and one-step keyboard adjustment use the existing save and apply path");
        monitors[0].SelectedBrightness = 39;
        await FlushSliderEditsAsync();
        check(panel.BrightnessSlider.Value == 39 && panel.BrightnessValue.Text == "39%",
            "Edits from the main controls immediately update the tray slider and label");
        panel.QuickNight.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await FlushSliderEditsAsync();
        check(selectedNightProfile && panel.QuickNight.IsChecked == true && panel.BrightnessSlider.Value == nightBrightness,
            "Tray Night selection applies the Night profile and loads its independent values");
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, 21.0);
        await FlushSliderEditsAsync();
        check(monitors[0].NightBrightness == 21 && monitors[0].DayBrightness == 39,
            "Tray Night edits preserve Day values");
        settings.AutoEnabled = true;
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, 22.0);
        await FlushSliderEditsAsync();
        check(settings.AutoEnabled && manualUntil == NextAutomaticTransition(),
            "Tray edits keep Auto enabled and expire at the next scheduled transition");
        settings.AutoEnabled = false;
        saveFailed = true;
        Message("Could not save settings. Automation paused: simulated write failure");
        check(panel.QuickError.Text.Contains("Could not save settings", StringComparison.Ordinal),
            "Tray controls surface settings-save failures");
        saveFailed = false;
        UpdateTrayControls();
        await RenderTrayAsync(panel, Path.Combine(previewDirectory, "tray-night.png"));

        panel.QuickDay.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        await FlushSliderEditsAsync();
        panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 1);
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, 64.0);
        panel.ContrastSlider.SetCurrentValue(Slider.ValueProperty, 52.0);
        panel.SaturationSlider.SetCurrentValue(Slider.ValueProperty, 57.0);
        await FlushSliderEditsAsync();
        check(settings.LinkMode == MonitorLinkMode.SameValues && panel.MonitorSelector.Visibility == Visibility.Collapsed
            && monitors.All(m => m.DayBrightness == 64 && m.CurrentBrightness == 64 && m.DayContrast == 52 && m.DaySaturation == 57),
            "Tray Mass controls update every supported display with the same values");
        panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 0);
        await FlushSliderEditsAsync();
        for (int i = 0; i < monitors.Count; i++) monitors[i].SelectedBrightness = 30 + i * 20;
        await FlushSliderEditsAsync();
        // A fresh Offset state makes the expected baseline explicit regardless of earlier smoke checks.
        settings.ModePresets.Remove(MonitorLinkMode.KeepOffsets);
        settings.DayOffsets = null;
        settings.NightOffsets = null;
        panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 2);
        await FlushSliderEditsAsync();
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, -10.0);
        await FlushSliderEditsAsync();
        check(panel.BrightnessSlider.Minimum == -100 && panel.BrightnessValue.Text == "-10"
            && monitors.Select(m => m.DayBrightness).SequenceEqual([20, 40, 60]),
            "Tray Offset accepts signed adjustments and preserves the displays' brightness differences");
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, 100.0);
        await FlushSliderEditsAsync();
        check(panel.BrightnessSlider.Value == 30 && monitors.Select(m => m.DayBrightness).SequenceEqual([60, 80, 100]),
            "Tray Offset thumb follows the shared hardware range limit");
        panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, -10.0);
        panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 0);
        await FlushSliderEditsAsync();
        check(panel.BrightnessSlider.Minimum == 0 && panel.BrightnessSlider.Value == 30
            && monitors.Select(m => m.DayBrightness).SequenceEqual([30, 50, 70]),
            "Switching from a negative tray offset restores Individual without a stale or coerced write");
        await RenderTrayAsync(panel, Path.Combine(previewDirectory, "tray-day.png"));
        check(panel.ActualHeight < 600 && panel.BrightnessSlider.Orientation == Orientation.Horizontal,
            "All three tray sliders fit in a compact panel");
        double savedMaxHeight = panel.MaxHeight;
        panel.MaxHeight = 360;
        panel.UpdateLayout();
        check(VisualChildren<ScrollViewer>(panel).Any(s => s.ScrollableHeight > 0),
            "Short work areas can scroll to every quick control and the full-settings button");
        panel.MaxHeight = savedMaxHeight;
        panel.UpdateLayout();

        var unavailable = new MonitorViewModel(DemoReadings()[0] with
            { Id = "tray-unsupported", Brightness = null, Contrast = null, Saturation = null }, new MonitorProfile(), ProfileChanged);
        settings.Monitors[unavailable.Id] = unavailable.Profile;
        monitors.Add(unavailable);
        UpdateLinkedControls();
        panel.QuickMonitor.SelectedItem = unavailable;
        check(!panel.BrightnessSlider.IsEnabled && !panel.ContrastSlider.IsEnabled && !panel.SaturationSlider.IsEnabled
            && pendingSliderEdits.Count == 0, "Tray disables unsupported controls without writing to the display");
        monitors.Remove(unavailable);
        settings.Monitors.Remove(unavailable.Id);
        UpdateLinkedControls();
        check(panel.QuickMonitor.SelectedItem is MonitorViewModel current && monitors.Contains(current)
            && panel.BrightnessSlider.IsEnabled, "Removing the selected tray display falls back to a connected display");

        var savedMonitors = monitors.ToArray();
        monitors.Clear();
        UpdateLinkedControls();
        check(!panel.BrightnessSlider.IsEnabled && !panel.ContrastSlider.IsEnabled && !panel.SaturationSlider.IsEnabled
            && panel.QuickHint.Text.StartsWith("No displays", StringComparison.Ordinal),
            "An empty monitor list leaves tray controls disabled with a refresh hint");
        foreach (var monitor in savedMonitors) monitors.Add(monitor);
        UpdateLinkedControls();
        check(panel.BrightnessSlider.IsEnabled, "Tray controls recover when displays reconnect");

        var work = new System.Drawing.Rectangle(-1920, 0, 1920, 1040);
        var position = TrayQuickControls.FitToWorkArea(new System.Drawing.Point(-1900, 10), work, 540, 780);
        check(position.X >= work.Left && position.Y >= work.Top && position.X + 540 <= work.Right && position.Y + 780 <= work.Bottom,
            "Tray positioning stays inside a secondary display with negative coordinates and scaled panel dimensions");
        panel.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(panel)!, Environment.TickCount, Key.Escape)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent });
        check(!panel.IsVisible && !exiting, "Escape dismisses quick controls without quitting SolarDesk");
        ShowTrayControls();
        ShowFromTray();
        check(!panel.IsVisible && IsVisible, "Opening full settings dismisses the tray panel and restores the main window");
        panel.CloseForExit();
        trayControls = null;
        trayBoundMode = null;
        trayBoundMonitor = null;
    }

    private static async Task RenderTrayAsync(TrayQuickControls panel, string output)
    {
        await panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        panel.UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(panel.ActualWidth), (int)Math.Ceiling(panel.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(panel);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output);
        encoder.Save(file);
    }
}
