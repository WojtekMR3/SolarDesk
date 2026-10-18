using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace SolarDesk;

public partial class MainWindow
{
    private Action<SunReminder>? showSunReminder;
    private readonly List<(UIElement Ray, TranslateTransform Drift, double X, double Y)> sunRays = [];
    private bool sunRayMotionRunning;

    private void InitializeSunFeatures()
    {
        DayRaysCheck.IsChecked = settings.DayRaysEnabled;
        SunRemindersCheck.IsChecked = settings.SunRemindersEnabled;
        DayRaysSurface.Visibility = settings.DayRaysEnabled ? Visibility.Visible : Visibility.Collapsed;
        // Short, thin strokes stay close to the small background sun.
        for (int i = 0; i < 16; i++)
        {
            double angle = (i * 22.5 + 7) * Math.PI / 180;
            double x = Math.Cos(angle), y = Math.Sin(angle);
            double inner = 34 + i % 3 * 3, outer = inner + 13 + i % 4 * 4;
            var drift = new TranslateTransform();
            var ray = new System.Windows.Shapes.Line
            {
                X1 = 120 + x * inner, Y1 = 70 + y * inner,
                X2 = 120 + x * outer, Y2 = 70 + y * outer,
                Stroke = new SolidColorBrush(Color.FromRgb(194, 129, 49)), StrokeThickness = i % 3 == 0 ? 1.6 : 1.1,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Opacity = 0.35, RenderTransform = drift
            };
            SunRayStreaks.Children.Add(ray);
            sunRays.Add((ray, drift, x, y));
        }
        Closed += (_, _) => StopSunRayMotion();
        // Diagnostics never send desktop notifications or write real settings.
        if (!diagnostic) showSunReminder = reminder => tray?.ShowBalloonTip(10000, reminder.Title, reminder.Message, Forms.ToolTipIcon.Info);
    }

    private void SunSettingClick(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.DayRaysEnabled = DayRaysCheck.IsChecked == true;
        settings.SunRemindersEnabled = SunRemindersCheck.IsChecked == true;
        DayRaysSurface.Visibility = settings.DayRaysEnabled ? Visibility.Visible : Visibility.Collapsed;
        UpdateSunRayMotion();
        SaveSettings();
    }

    private void UpdateSunRayMotion()
    {
        bool shouldMove = settings.DayRaysEnabled && !IsNightAppearance && PresentationActive && SystemParameters.ClientAreaAnimation;
        if (shouldMove == sunRayMotionRunning) return;
        if (!shouldMove) { StopSunRayMotion(); return; }
        sunRayMotionRunning = true;
        for (int i = 0; i < sunRays.Count; i++)
        {
            var (ray, drift, x, y) = sunRays[i];
            double duration = 7 + i % 5 * 1.3;
            double start = 0.22 + i % 4 * 0.06;
            var shine = new DoubleAnimationUsingKeyFrames
            { Duration = TimeSpan.FromSeconds(duration), RepeatBehavior = RepeatBehavior.Forever };
            shine.KeyFrames.Add(SoftFrame(start, 0));
            shine.KeyFrames.Add(SoftFrame(0.58, duration * 0.4));
            shine.KeyFrames.Add(SoftFrame(start, duration));
            Timeline.SetDesiredFrameRate(shine, 20);
            ray.BeginAnimation(OpacityProperty, shine);
            var travel = new DoubleAnimation(0, 3, TimeSpan.FromSeconds(duration / 2))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };
            Timeline.SetDesiredFrameRate(travel, 20);
            var horizontal = travel.Clone(); horizontal.To = x * 3;
            var vertical = travel.Clone(); vertical.To = y * 3;
            drift.BeginAnimation(TranslateTransform.XProperty, horizontal);
            drift.BeginAnimation(TranslateTransform.YProperty, vertical);
        }
    }

    private void StopSunRayMotion()
    {
        foreach (var (ray, drift, _, _) in sunRays)
        {
            ray.BeginAnimation(OpacityProperty, null);
            drift.BeginAnimation(TranslateTransform.XProperty, null);
            drift.BeginAnimation(TranslateTransform.YProperty, null);
            ray.Opacity = 0.35;
            drift.X = drift.Y = 0;
        }
        sunRayMotionRunning = false;
    }

    private void CheckSunReminders(DateTimeOffset now)
    {
        if (showSunReminder is null || !settings.SunRemindersEnabled || !settings.HasLocation || exiting) return;
        var reminder = SunReminders.FindDue(now, settings, schedule.GetSolarEvents(now, settings),
            manualUntil > now || pauseUntil > now, monitors.Any(m => m.Enabled && m.CanControl));
        if (reminder is null) return;
        // Persist first so reopening the app inside the delivery window cannot repeat the notification.
        settings.RecentSunReminders.Add(reminder.Key);
        if (SaveSettings()) showSunReminder(reminder);
    }

    private async Task CheckSunFeaturesAsync(Action<bool, string> check, string directory)
    {
        if (!diagnostic) throw new InvalidOperationException("Sun feature checks require demo mode.");
        DayRaysCheck.IsChecked = false; SunRemindersCheck.IsChecked = false;
        SunSettingClick(this, new RoutedEventArgs());
        check(!settings.DayRaysEnabled && !settings.SunRemindersEnabled && DayRaysSurface.Visibility == Visibility.Collapsed,
            "Settings switches disable sun rays and reminders independently of weather");
        check(!sunRayMotionRunning && sunRays.All(r => !r.Drift.HasAnimatedProperties), "Disabling sun rays removes their animation clocks");
        var delivered = new List<SunReminder>();
        showSunReminder = delivered.Add;
        var sunEvent = schedule.GetSolarEvents(DateTimeOffset.UtcNow, settings).First();
        var dueAt = sunEvent.At.AddMinutes(-30);
        CheckSunReminders(dueAt);
        check(delivered.Count == 0, "Disabled reminders never reach the notification transport");
        SunRemindersCheck.IsChecked = true;
        SunSettingClick(this, new RoutedEventArgs());
        CheckSunReminders(dueAt); CheckSunReminders(dueAt.AddSeconds(10));
        check(delivered.Count == 1 && settings.RecentSunReminders.Contains(delivered[0].Key),
            "Scheduler delivers one reminder and records it before subsequent ticks");
        showSunReminder = null;
        DayRaysCheck.IsChecked = true; SunSettingClick(this, new RoutedEventArgs());
        check(DayRaysSurface.IsVisible && !DayRaysSurface.IsHitTestVisible, "Day rays can be restored and never intercept monitor controls");
        UpdateLayout();
        check(DayRaysSurface.ActualWidth <= 241 && DayRaysSurface.ActualHeight <= 221, "Sun and short rays stay inside a compact background area");
        check(sunRayMotionRunning == SystemParameters.ClientAreaAnimation, "Sun ray motion follows the Windows animation preference");
        await RenderAsync(Path.Combine(directory, "sun-rays-start.png"));
        await Task.Delay(1200);
        await RenderAsync(Path.Combine(directory, "sun-rays-shine.png"));
        NightTab.IsChecked = true;
        check(!sunRayMotionRunning, "Sun ray animation stops in Night mode");
        DayTab.IsChecked = true;
        Hide();
        check(!sunRayMotionRunning, "Sun ray animation stops while the window is hidden");
        Show();
        SettingsClick(this, new RoutedEventArgs());
        await RenderAsync(Path.Combine(directory, "sun-settings.png"));
        check(DayRaysCheck.IsVisible && SunRemindersCheck.IsVisible, "Both sun settings are visible in the settings panel");
        CloseSettings();
    }
}
