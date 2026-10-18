using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckTrayAvailabilityAsync(Action<bool, string> check)
    {
        var window = new MainWindow(true) { diagnosticNow = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero) };
        var firstScan = new TaskCompletionSource<List<MonitorReading>>();
        var recoveryScan = new TaskCompletionSource<List<MonitorReading>>();
        int scans = 0;
        window.diagnosticScan = () => ++scans == 1 ? firstScan.Task : recoveryScan.Task;
        try
        {
            var initialize = window.InitializeAsync();
            window.ShowTrayControls();
            var panel = window.trayControls!;
            check(!window.IsVisible && panel.IsVisible && panel.BrightnessValue.Text.StartsWith("Detecting", StringComparison.Ordinal)
                && !panel.QuickRefresh.IsEnabled && scans == 1,
                "Tray opened during startup shows detection progress and does not start another scan");
            firstScan.SetResult([]);
            await initialize;
            check(panel.BrightnessValue.Text == "Unavailable" && panel.QuickRefresh.IsEnabled,
                "Empty discovery leaves a refresh action available in the tray");
            panel.Hide();
            window.ShowTrayControls();
            check(scans == 2 && window.scanningDisplays && !window.IsVisible,
                "Reopening the tray retries an empty startup scan without opening the full window");
            recoveryScan.SetResult(DemoReadings());
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            var primary = window.monitors.First(m => m.IsPrimary);
            check(panel.BrightnessSlider.IsEnabled && panel.BrightnessValue.Text == primary.SelectedBrightnessText
                && panel.QuickMonitor.SelectedItem == primary && !window.IsVisible,
                "Recovered brightness values appear automatically in a tray-only session");
            window.ShowTrayControls();
            check(scans == 2, "Opening healthy tray controls does not rescan monitors");

            // Recreate a hidden main window whose settings overlay disabled its control surface.
            window.SettingsClick(window, new RoutedEventArgs());
            panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 1);
            await window.FlushSliderEditsAsync();
            check(!window.MainSurface.IsEnabled && panel.BrightnessSlider.IsEnabled && panel.ContrastSlider.IsEnabled,
                "Mass tray controls stay usable while the hidden full-settings overlay disables its own surface");
            panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, 61d);
            await window.FlushSliderEditsAsync();
            check(window.monitors.All(m => m.SelectedBrightness == 61) && panel.BrightnessValue.Text == "61%",
                "Mass tray values apply directly to monitor profiles while the main surface is disabled");
            panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 2);
            await window.FlushSliderEditsAsync();
            int[] baseline = window.monitors.Select(m => m.SelectedBrightness).ToArray();
            panel.BrightnessSlider.SetCurrentValue(Slider.ValueProperty, -5d);
            await window.FlushSliderEditsAsync();
            check(panel.BrightnessValue.Text == "-5" && window.monitors.Select(m => m.SelectedBrightness).SequenceEqual(baseline.Select(v => v - 5)),
                "Offset tray controls remain independent of hidden main-window controls");
            panel.QuickMode.SetCurrentValue(Selector.SelectedIndexProperty, 0);
            await window.FlushSliderEditsAsync();
            check(panel.BrightnessSlider.Minimum == 0 && panel.BrightnessValue.Text == primary.SelectedBrightnessText,
                "Returning to Individual replaces shared values with the selected display's saved brightness");

            panel.QuickMonitor.SelectedItem = window.monitors.First(m => m.DisplayNumber == "2");
            var selectedId = ((MonitorViewModel)panel.QuickMonitor.SelectedItem).Id;
            window.diagnosticScan = () =>
            {
                scans++;
                return Task.FromResult(DemoReadings().Select(reading => reading with
                {
                    Left = reading.Display == "DISPLAY2" ? 3440 : reading.Display == "DISPLAY3" ? -1920 : 0
                }).ToList());
            };
            await panel.RefreshDisplays!();
            check(scans == 3 && panel.QuickRefresh.IsEnabled && panel.BrightnessSlider.IsEnabled && !window.IsVisible,
                "Tray Refresh displays rediscovers monitors without opening the full window");
            check(panel.QuickMonitor.Items.Cast<MonitorViewModel>().Select(m => m.DisplayNumber).SequenceEqual(["3", "1", "2"])
                && ((MonitorViewModel)panel.QuickMonitor.SelectedItem).Id == selectedId
                && ((MonitorViewModel)panel.QuickMonitor.Items[1]).IsPrimary,
                "Refreshing a changed desktop layout reorders tray buttons while preserving the selected display and primary marker");
            window.monitors.Clear();
            window.UpdateLinkedControls();
            window.ShowTrayControls();
            check(scans == 3, "Repeated tray openings throttle automatic recovery scans for thirty seconds");
            window.diagnosticNow = window.CurrentTime.AddSeconds(31);
            window.ShowTrayControls();
            check(scans == 4 && panel.BrightnessSlider.IsEnabled, "Tray retries missing monitors after the recovery cooldown");
        }
        finally
        {
            window.exiting = true;
            window.timer.Stop(); window.saveTimer.Stop();
            window.weatherCancellation?.Cancel();
            window.trayControls?.CloseForExit();
            await window.hardware.DisposeAsync();
            window.Close();
        }
    }
}
