using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace SolarDesk;

public partial class MainWindow
{
    private async Task CheckBackgroundActivityAsync(Action<bool, string> check)
    {
        var window = new MainWindow(true) { ShowActivated = false, diagnosticForeground = false,
            diagnosticNow = new DateTimeOffset(2026, 10, 18, 12, 0, 0, TimeSpan.Zero) };
        try
        {
            int weatherRequests = 0;
            CancellationToken requestToken = default;
            window.fetchWeather = async (_, _, token) =>
            {
                weatherRequests++;
                requestToken = token;
                await Task.Delay(Timeout.Infinite, token);
                throw new InvalidOperationException("A cancelled test request must not finish.");
            };
            await window.InitializeAsync();
            nint handle = new WindowInteropHelper(window).EnsureHandle();
            check(handle != 0 && !window.IsVisible && window.monitors.Count == 3 && weatherRequests == 0,
                "Tray startup initializes monitors and a shortcut message handle without showing the window or fetching weather");
            check(window.timer.Interval == TimeSpan.FromSeconds(60), "Hidden idle scheduler wakes once per minute");
            int scans = window.diagnosticScanCount;
            var firstMonitor = window.monitors[0];
            window.diagnosticNow = window.CurrentTime.AddMinutes(6);
            await window.CycleAsync();
            check(window.diagnosticScanCount == scans && ReferenceEquals(window.monitors[0], firstMonitor),
                "Hidden scheduler preserves display models and skips the five-minute hardware rescan");
            window.DisplaysChanged(null, EventArgs.Empty);
            await window.Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            check(window.refreshRequested && window.timer.Interval == TimeSpan.FromSeconds(1),
                "Display-change events schedule a short coalesced refresh instead of waiting for the idle timer");
            await window.CycleAsync();
            check(window.diagnosticScanCount == scans + 1 && !ReferenceEquals(window.monitors[0], firstMonitor),
                "Requested display refresh still scans immediately while hidden");
            window.Show();
            window.diagnosticForeground = true;
            window.PresentationChanged();
            check(weatherRequests == 1 && window.weatherCancellation is not null, "Foreground window starts its deferred weather request");
            window.SelectProfileEditor(true);
            check(window.starMotionRunning == SystemParameters.ClientAreaAnimation, "Foreground night window respects animation preference");
            window.diagnosticForeground = false;
            window.PresentationChanged();
            await Task.Yield();
            check(!window.starMotionRunning && !window.sunRayMotionRunning && window.WeatherParticles.Children.Count == 0
                && !window.SunRise.HasAnimatedProperties && !window.MoonRise.HasAnimatedProperties
                && !((RadialGradientBrush)window.NightGlowSurface.Background).HasAnimatedProperties,
                "Losing focus removes decorative animation clocks, including theme transitions");
            check(requestToken.IsCancellationRequested && window.weatherCancellation is null,
                "Losing focus cancels the in-flight weather request");
            await window.RefreshWeatherAsync();
            check(weatherRequests == 1, "Inactive window makes no background weather requests");
            window.fetchWeather = (_, _, _) => Task.FromResult(new WeatherSnapshot(14, 61, true, DateTimeOffset.UtcNow));
            window.diagnosticForeground = true;
            window.PresentationChanged();
            check(window.weather?.Code == 61 && window.WeatherParticles.Children.Count == (SystemParameters.ClientAreaAnimation ? 22 : 0),
                "Returning to the window refreshes weather and resumes enabled effects");
            window.WindowState = WindowState.Minimized;
            check(!window.starMotionRunning && window.WeatherParticles.Children.Count == 0,
                "Minimizing stops all continuous effects");
            window.WindowState = WindowState.Normal;
            window.SelectProfileEditor(false);
            check(window.sunRayMotionRunning == SystemParameters.ClientAreaAnimation, "Day rays resume when the window is restored");
            window.diagnosticNow = window.CurrentTime.AddMinutes(6);
            await window.CycleAsync();
            check(window.diagnosticScanCount == scans + 2, "Foreground use reconciles stale monitor readings");
            window.Hide();
            check(!window.sunRayMotionRunning && window.timer.Interval == TimeSpan.FromSeconds(60),
                "Closing to the tray returns to quiet background operation");
        }
        finally
        {
            window.exiting = true;
            window.timer.Stop(); window.saveTimer.Stop();
            window.weatherCancellation?.Cancel();
            await window.hardware.DisposeAsync();
            window.Close();
        }
    }
}
