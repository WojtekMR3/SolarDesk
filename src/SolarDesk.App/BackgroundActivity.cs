using System.Windows;
using System.Windows.Media;

namespace SolarDesk;

public partial class MainWindow
{
    private bool PresentationActive => IsVisible && WindowState != WindowState.Minimized && !exiting
        && (diagnosticForeground ?? (diagnostic || IsActive));

    private void InitializeBackgroundActivity()
    {
        Activated += (_, _) => PresentationChanged();
        Deactivated += (_, _) => PresentationChanged();
        IsVisibleChanged += (_, _) => PresentationChanged();
        StateChanged += (_, _) => PresentationChanged();
    }

    private void PresentationChanged()
    {
        UpdateStarMotion();
        UpdateSunRayMotion();
        UpdateWeatherEffects();
        if (!PresentationActive)
        {
            // Discard in-flight weather so returning to the window can request fresh data.
            if (weatherCancellation is not null)
            {
                weatherCancellation.Cancel();
                weatherCancellation = null;
                nextWeatherRefresh = DateTimeOffset.MinValue;
            }
            // Finish short theme transitions too, leaving no decorative animation clocks.
            AnimateEmblem(SunEmblem, SunRise, !IsNightAppearance, false);
            AnimateEmblem(MoonEmblem, MoonRise, IsNightAppearance, false);
            foreach (var surface in new[] { DayGlowSurface, NightGlowSurface })
                if (surface.Background is RadialGradientBrush glow)
                {
                    glow.BeginAnimation(RadialGradientBrush.CenterProperty, null);
                    glow.BeginAnimation(RadialGradientBrush.GradientOriginProperty, null);
                }
        }
        else
        {
            _ = RefreshWeatherAsync();
            // Reconcile stale hardware readings when the full window is next used.
            if (ready && !diagnostic && CurrentTime - lastScan >= TimeSpan.FromMinutes(5)) _ = CycleAsync();
        }
        UpdateCycleInterval();
    }

    private void UpdateCycleInterval()
    {
        if (!ready || exiting) return;
        timer.Interval = refreshRequested ? TimeSpan.FromSeconds(1)
            : schedule.NextUpdateDelay(CurrentTime, settings, PresentationActive,
                manualDayAmount.HasValue, manualUntil, pauseUntil);
    }
}
