using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckProfileModesAsync(Action<bool, string> check, string directory)
    {
        static DateTimeOffset At(int hour) => new(2026, 10, 18, hour, 0, 0, TimeSpan.FromHours(2));
        static AppSettings Saved(bool auto, bool night) => JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(new AppSettings
        {
            AutoEnabled = auto, SelectedNightProfile = night, ManualProfileSelected = true, Appearance = AppAppearance.Night,
            Latitude = 52.2297, Longitude = 21.0122, LocationName = "Warsaw", TimeZoneId = "Central European Standard Time",
            Monitors = DemoReadings().ToDictionary(r => r.Id, r => new MonitorProfile
            {
                DayBrightness = 80, NightBrightness = 20, DayContrast = 60, NightContrast = 40,
                DaySaturation = 70, NightSaturation = 30, SaturationSource = r.SaturationSource,
                OriginalSaturationRaw = 50, OriginalVibranceRaw = 50, AdvancedManaged = true,
                DayAdvanced = new() { Sharpness = 8 }, NightAdvanced = new() { Sharpness = 3 },
                Manual = new(42, 51, 57), ManualAdvanced = new() { Sharpness = 5 }
            })
        }))!;
        static List<MonitorWriteResult> Success(IEnumerable<MonitorTarget> targets)
            => targets.Select(t => new MonitorWriteResult(t.Id, true, "Demo only")).ToList();
        static async Task CloseDemo(MainWindow window)
        {
            window.CancelQueuedPreview(); window.exiting = true;
            await window.hardware.DisposeAsync(); window.Close();
        }

        foreach (bool auto in new[] { true, false })
        foreach (int hour in new[] { 15, 2 })
        {
            bool savedNight = hour == 15;
            var startup = new MainWindow(true, Saved(auto, savedNight)) { diagnosticNow = At(hour) };
            var applied = new List<MonitorTarget>();
            startup.diagnosticWrite = targets => { applied.AddRange(targets); return Task.FromResult(Success(targets)); };
            try
            {
                await startup.InitializeAsync();
                bool day = auto ? hour == 15 : !savedNight;
                check(applied.Count == 3 && applied.All(t => t.Brightness == (day ? 80 : 20)
                    && t.Contrast == (day ? 60 : 40) && t.Saturation == (day ? 70 : 30)
                    && t.Advanced?.Sharpness == (day ? 8u : 3u)) && startup.settings.AutoEnabled == auto
                    && startup.selectedNightProfile == !day && !startup.settings.ManualProfileSelected
                    && startup.IsNightAppearance == !day && startup.settings.Appearance == AppAppearance.FollowProfile
                    && startup.DayAtmosphere.Opacity == (day ? 1 : 0) && startup.NightAtmosphere.Opacity == (day ? 0 : 1),
                    $"Startup at {hour:00}:00 with Auto {(auto ? "on follows the current sun profile" : "off restores the selected profile")}, including all controls");
            }
            finally { await CloseDemo(startup); }
        }

        var window = new MainWindow(true, Saved(auto: true, night: true)) { diagnosticNow = At(15) };
        var writes = new List<MonitorTarget>();
        window.diagnosticWrite = targets => { writes.AddRange(targets); return Task.FromResult(Success(targets)); };
        try
        {
            await window.InitializeAsync(); window.Show();
            var main = window.monitors.First(m => m.IsPrimary);
            check(window.DayTab.IsVisible && window.NightTab.IsVisible && window.FindName("ManualTab") is null
                && window.FindName("ManualAppearancePanel") is null && window.FindName("AppearanceBox") is null,
                "The main controls contain Day, Night and Auto without a separate Manual tab");
            main.SelectedBrightness = 78; main.SelectedContrast = 66; main.SelectedSaturation = 58;
            await window.FlushSliderEditsAsync();
            var boundary = window.schedule.NextTransitionBoundary(At(15), window.settings);
            check(window.settings.AutoEnabled && window.AutomationButton.IsChecked == true && window.manualUntil == boundary,
                "Editing all three controls keeps Auto on until the next sun transition");
            window.FadeSlider.Value = 60;
            check(window.manualUntil == boundary!.Value.AddMinutes(-15),
                "Changing fade duration updates the next transition for an active edit");
            window.FadeSlider.Value = 30;
            window.diagnosticNow = At(16); await window.CycleAsync();
            main = window.monitors.First(m => m.IsPrimary);
            check(main.CurrentBrightness == 78 && window.settings.AutoEnabled && window.manualDayAmount.HasValue,
                "Daytime edits remain active after one hour without disabling Auto");
            window.NightTab.IsChecked = true;
            check(window.settings.AutoEnabled && main.CurrentBrightness == 20 && main.SelectedBrightness == 20,
                "Choosing Night previews its settings while Auto remains enabled");
            window.DayTab.IsChecked = true;
            check(window.settings.AutoEnabled && main.CurrentBrightness == 78,
                "Returning to Day restores its edited values without switching Auto off");
            window.diagnosticNow = boundary!.Value.AddMinutes(5);
            writes.Clear(); await window.CycleAsync(); main = window.monitors.First(m => m.IsPrimary);
            double amount = window.schedule.Evaluate(window.CurrentTime, window.settings).DayAmount;
            check(window.manualDayAmount is null && writes.Count == 3 && writes.Single(t => t.Id == main.Id).Brightness
                == SolarSchedule.Interpolate(20, 78, amount) && amount > 0 && amount < 1,
                "The next sunset fade automatically takes over from a profile edit");
            window.diagnosticNow = At(22); await window.CycleAsync(); main = window.monitors.First(m => m.IsPrimary);
            check(window.selectedNightProfile && main.CurrentBrightness == 20, "A missed sunset is caught up on the next scheduler cycle");
            window.AutomationClick(window, new RoutedEventArgs());
            check(!window.settings.AutoEnabled && window.selectedNightProfile && main.CurrentBrightness == 20
                && window.manualUntil is null && window.DayTab.IsVisible && window.NightTab.IsVisible,
                "Turning Auto off holds the focused Night profile and keeps both tabs available");
            window.diagnosticNow = At(15).AddDays(1); await window.CycleAsync(); main = window.monitors.First(m => m.IsPrimary);
            check(window.selectedNightProfile && main.CurrentBrightness == 20 && !window.settings.AutoEnabled,
                "With Auto off, Night remains active through the following daytime");
            window.DayTab.IsChecked = true;
            check(main.CurrentBrightness == 78 && main.SelectedContrast == 66 && main.SelectedSaturation == 58 && !window.settings.AutoEnabled
                && window.DayAtmosphere.Opacity == 1 && window.NightAtmosphere.Opacity == 0 && !window.IsNightAppearance,
                "With Auto off, selecting Day applies its saved values and restores the warm daytime background");
            main.SelectedBrightness = 82; main.SelectedContrast = 67; main.SelectedSaturation = 60;
            main.SetAdvanced(main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Sharpness).Control, 9);
            await window.FlushSliderEditsAsync();
            window.NightTab.IsChecked = true;
            check(main.CurrentBrightness == 20 && main.SelectedContrast == 40 && main.SelectedSaturation == 30
                && main.SelectedAdvanced.Sharpness == 3 && main.Profile.Manual == new ControlValues(42, 51, 57)
                && window.NightAtmosphere.Opacity == 1 && window.DayAtmosphere.Opacity == 0 && window.IsNightAppearance,
                "Day edits do not overwrite Night or archived Manual values");
            main.SelectedBrightness = 24; main.SelectedContrast = 44; main.SelectedSaturation = 34;
            main.SetAdvanced(main.AdvancedControls.First(c => c.Control.Kind == AdvancedKind.Sharpness).Control, 4);
            await window.FlushSliderEditsAsync(); window.DayTab.IsChecked = true;
            check(main.CurrentBrightness == 82 && main.SelectedContrast == 67 && main.SelectedSaturation == 60
                && main.SelectedAdvanced.Sharpness == 9 && window.manualUntil is null,
                "Switching back restores Day main and additional controls without an expiry");
            window.ApplyProfileTheme(animate: false);
            await window.RenderAsync(Path.Combine(directory, "day-auto-off.png"));
            window.NightTab.IsChecked = true; window.ApplyProfileTheme(animate: false);
            await window.RenderAsync(Path.Combine(directory, "night-auto-off.png"));
            window.SettingsClick(window, new RoutedEventArgs());
            await window.RenderAsync(Path.Combine(directory, "profile-settings.png"));
            window.CloseSettings(); window.Width = 820;
            await window.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); window.UpdateLayout();
            check(window.NightTab.TranslatePoint(new Point(window.NightTab.ActualWidth, 0), window).X
                    < window.LinkModeBox.TranslatePoint(new Point(), window).X
                && window.LinkModeBox.TranslatePoint(new Point(window.LinkModeBox.ActualWidth, 0), window).X
                    < window.AutomationButton.TranslatePoint(new Point(), window).X,
                "Day/Night, sync mode and Auto fit without overlap at minimum window width");
            await window.RenderAsync(Path.Combine(directory, "profiles-small.png"));

            var persisted = JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(window.settings))!;
            var restarted = new MainWindow(true, persisted) { diagnosticNow = At(15) };
            try
            {
                await restarted.InitializeAsync(); var restored = restarted.monitors.First(m => m.IsPrimary);
                check(!restarted.settings.AutoEnabled && restarted.selectedNightProfile && restarted.IsNightAppearance
                    && restored.CurrentBrightness == 24 && restored.SelectedContrast == 44 && restored.SelectedSaturation == 34
                    && restored.SelectedAdvanced.Sharpness == 4,
                    "Restart with Auto off keeps the selected Night profile even at 15:00");
                restarted.AutomationClick(restarted, new RoutedEventArgs());
                check(restarted.settings.AutoEnabled && !restarted.selectedNightProfile && !restarted.IsNightAppearance
                    && restored.CurrentBrightness == 82 && restored.SelectedContrast == 67 && restored.SelectedSaturation == 60,
                    "Turning Auto on at 15:00 immediately applies Day and selects its tab");
                restarted.AutomationClick(restarted, new RoutedEventArgs());
                check(!restarted.settings.AutoEnabled && !restarted.selectedNightProfile && restored.CurrentBrightness == 82,
                    "Turning Auto off again keeps the focused Day values");
                restored.SelectedBrightness = 49;
                await restarted.RestoreAsync(); await restarted.FlushSliderEditsAsync();
                check(restarted.manualDayAmount is null && !restarted.settings.AutoEnabled,
                    "Restore originals cancels queued profile edits without reapplying the held profile");
            }
            finally { await CloseDemo(restarted); }
        }
        finally { await CloseDemo(window); }
    }
}
