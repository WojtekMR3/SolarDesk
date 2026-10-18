using System.Windows;
using System.Windows.Controls.Primitives;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckTrayAutoAsync(Action<bool, string> check)
    {
        var window = new MainWindow(true) { diagnosticNow = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero) };
        try
        {
            window.settings.AutoEnabled = false;
            await window.InitializeAsync();
            window.ShowTrayControls();
            var panel = window.trayControls!;
            check(panel.QuickAuto.IsChecked == false && panel.QuickAuto.IsEnabled && !window.IsVisible,
                "Tray Auto opens with the saved automation state without showing the main window");

            // Exercise the actual click handler and await its asynchronous scheduling operation.
            var toggle = panel.ToggleAutomation!;
            Task action = Task.CompletedTask;
            panel.ToggleAutomation = () => action = toggle();
            async Task ClickAuto()
            {
                panel.QuickAuto.SetCurrentValue(ToggleButton.IsCheckedProperty, panel.QuickAuto.IsChecked != true);
                panel.QuickAuto.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                await action;
            }

            window.SettingsClick(window, new RoutedEventArgs());
            await ClickAuto();
            double amount = window.schedule.Evaluate(window.CurrentTime, window.settings).DayAmount;
            check(window.settings.AutoEnabled && panel.QuickAuto.IsChecked == true && window.AutomationButton.IsChecked == true
                && !window.MainSurface.IsEnabled && !window.IsVisible && window.manualDayAmount is null
                && window.monitors.All(m => m.CurrentBrightness == SolarSchedule.Interpolate(m.NightBrightness, m.DayBrightness, amount)),
                "Tray Auto enables and applies the sun schedule even when hidden main-window controls are disabled");

            await window.ApplySelectedProfileAsync(true);
            check(panel.QuickAuto.IsChecked == true && window.manualUntil.HasValue,
                "A tray profile preview keeps Auto enabled until the next scheduled transition");
            await ClickAuto();
            check(!window.settings.AutoEnabled && panel.QuickAuto.IsChecked == false && window.AutomationButton.IsChecked == false
                && window.selectedNightProfile && window.manualUntil is null
                && window.monitors.All(m => m.CurrentBrightness == m.NightBrightness),
                "Turning tray Auto off keeps the selected profile and clears the automatic transition deadline");

            window.AutomationClick(window, new RoutedEventArgs());
            check(panel.QuickAuto.IsChecked == true && window.settings.AutoEnabled,
                "The main Auto action immediately updates the open tray toggle");
            panel.Hide();
            await window.ToggleAutomationAsync();
            window.ShowTrayControls();
            check(panel.QuickAuto.IsChecked == false && !window.settings.AutoEnabled,
                "Reopening tray controls refreshes Auto changes made while the panel was hidden");

            foreach (var monitor in window.monitors) monitor.Profile.Enabled = false;
            window.UpdateTrayControls();
            check(!panel.QuickAuto.IsEnabled && panel.QuickAuto.ToolTip.ToString()!.Contains("Enable at least one display", StringComparison.Ordinal),
                "Tray Auto explains why it cannot start without an enabled controllable display");
            await window.ToggleAutomationAsync();
            check(!window.settings.AutoEnabled && panel.QuickAuto.IsChecked == false && window.AutomationButton.IsChecked == false,
                "A rejected Auto request leaves both toggles off");

            foreach (var monitor in window.monitors) monitor.Profile.Enabled = true;
            window.settings.Latitude = window.settings.Longitude = null;
            window.CloseSettings();
            window.UpdateTrayControls();
            await ClickAuto();
            check(window.IsVisible && !panel.IsVisible && window.LocationLayer.Visibility == Visibility.Visible
                && window.enableAfterLocation && !window.settings.AutoEnabled && panel.QuickAuto.IsChecked == false,
                "Tray Auto opens location setup when needed without claiming automation is already enabled");
            window.DismissLocation();
            check(!window.settings.AutoEnabled && !window.enableAfterLocation,
                "Dismissing location setup leaves Auto off");
        }
        finally
        {
            window.exiting = true;
            window.CancelQueuedPreview();
            window.timer.Stop(); window.saveTimer.Stop();
            window.weatherCancellation?.Cancel();
            window.trayControls?.CloseForExit();
            await window.hardware.DisposeAsync();
            window.Close();
        }
    }
}
