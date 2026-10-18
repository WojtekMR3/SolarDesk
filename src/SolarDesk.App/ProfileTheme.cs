using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace SolarDesk;

public partial class MainWindow
{
    private readonly List<(UIElement Star, TranslateTransform Drift, double BaseOpacity)> movingStars = [];
    private bool starMotionRunning;

    private void InitializeStarMotion()
    {
        foreach (UIElement star in StarField.Children)
        {
            var drift = new TranslateTransform();
            star.RenderTransform = drift;
            movingStars.Add((star, drift, star.Opacity));
        }
        SystemParameters.StaticPropertyChanged += MotionPreferenceChanged;
        Closed += (_, _) =>
        {
            StopStarMotion();
            SystemParameters.StaticPropertyChanged -= MotionPreferenceChanged;
        };
    }

    private void MotionPreferenceChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.ClientAreaAnimation))
            Dispatcher.InvokeAsync(() => { UpdateStarMotion(); UpdateSunRayMotion(); UpdateWeatherEffects(); });
    }

    private void UpdateStarMotion()
    {
        bool shouldMove = IsNightAppearance && PresentationActive && SystemParameters.ClientAreaAnimation;
        if (shouldMove == starMotionRunning) return;
        if (!shouldMove) { StopStarMotion(); return; }

        starMotionRunning = true;
        for (int i = 0; i < movingStars.Count; i++)
        {
            var (star, drift, opacity) = movingStars[i];
            var random = diagnostic ? new Random(8500 + i) : Random.Shared;
            star.BeginAnimation(OpacityProperty, StarTwinkle(opacity, random));
            drift.BeginAnimation(TranslateTransform.XProperty, StarDrift(2 + random.NextDouble() * 4, random));
            drift.BeginAnimation(TranslateTransform.YProperty, StarDrift(1 + random.NextDouble() * 3, random));
        }
    }

    private static DoubleAnimationUsingKeyFrames StarTwinkle(double opacity, Random random)
    {
        double duration = 47 + random.NextDouble() * 38;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(duration), RepeatBehavior = RepeatBehavior.Forever };
        double first = opacity * (0.5 + random.NextDouble() * 0.5);
        animation.KeyFrames.Add(SoftFrame(first, 0));
        double at = 0;
        // Each star gets a long, uneven sequence of glints and quiet pauses.
        while (at < duration - 5)
        {
            at += 0.8 + random.NextDouble() * 3.8;
            double value = Math.Min(0.9, opacity * (0.25 + random.NextDouble()));
            animation.KeyFrames.Add(SoftFrame(value, at));
            if (random.NextDouble() < 0.3 && at < duration - 10)
            {
                at += 1.5 + random.NextDouble() * 3;
                animation.KeyFrames.Add(SoftFrame(value, at));
            }
        }
        animation.KeyFrames.Add(SoftFrame(first, duration));
        Timeline.SetDesiredFrameRate(animation, 24);
        return animation;
    }

    private static DoubleAnimationUsingKeyFrames StarDrift(double amplitude, Random random)
    {
        double duration = 23 + random.NextDouble() * 24;
        var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromSeconds(duration), RepeatBehavior = RepeatBehavior.Forever };
        double first = (random.NextDouble() * 2 - 1) * amplitude;
        animation.KeyFrames.Add(SoftFrame(first, 0));
        animation.KeyFrames.Add(SoftFrame(amplitude, duration * (0.18 + random.NextDouble() * 0.12)));
        animation.KeyFrames.Add(SoftFrame(-amplitude, duration * (0.55 + random.NextDouble() * 0.15)));
        animation.KeyFrames.Add(SoftFrame(first, duration));
        Timeline.SetDesiredFrameRate(animation, 24);
        return animation;
    }

    private static EasingDoubleKeyFrame SoftFrame(double value, double seconds) => new(value, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(seconds)))
    { EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } };

    private void StopStarMotion()
    {
        foreach (var (star, drift, _) in movingStars)
        {
            double opacity = star.Opacity, x = drift.X, y = drift.Y;
            star.BeginAnimation(OpacityProperty, null);
            drift.BeginAnimation(TranslateTransform.XProperty, null);
            drift.BeginAnimation(TranslateTransform.YProperty, null);
            star.Opacity = opacity; drift.X = x; drift.Y = y;
        }
        starMotionRunning = false;
    }

    private void ApplyProfileTheme(bool animate)
    {
        bool night = IsNightAppearance;
        bool motion = animate && PresentationActive && SystemParameters.ClientAreaAnimation;
        var palette = new (string Key, string Day, string Night)[]
        {
            ("Text", "#36332F", "#EDF0FF"),
            ("Muted", "#6F655C", "#A6B2CC"),
            ("Accent", "#DDB77F", "#BBC5FF"),
            ("OnAccent", "#493A2C", "#171B31"),
            ("WindowBase", "#EBDBC6", "#0A1020"),
            ("Surface", "#F4EAD9", "#141D30"),
            ("SurfaceBorder", "#D3BEA5", "#2B3651"),
            ("PrimaryBorder", "#B77C4F", "#91A5E0"),
            ("ContrastThumb", "#D98C4D", "#D4DBFF"),
            ("RaisedSurface", "#E5CFB5", "#26314B"),
            ("TabRail", "#E5CDA9", "#10192B"),
            ("TabSurface", "#ECD9BC", "#202D49"),
            ("TabBorder", "#C5AC89", "#54658C"),
            ("TabHover", "#DFBE99", "#304265"),
            ("SelectedTab", "#F7EDDD", "#B8C5FF"),
            ("SelectedTabText", "#473B30", "#141A30"),
            ("SelectedTabBorder", "#CFB18B", "#D1D9FF"),
            ("SubtleSurface", "#EFE0CA", "#121D31"),
            ("Divider", "#DCC9B0", "#303C57"),
            ("TrackBrush", "#D3C8B6", "#354260"),
            ("BrightnessThumb", "#E5BE76", "#BBC5FF"),
            ("AccentText", "#7B5939", "#BBC5FF"),
            ("ControlFill", "#8B947D", "#C1CDE7"),
            ("SaturationFill", "#3C9158", "#70BD88"),
            ("ValueText", "#50463B", "#CAD3EA"),
            ("ThumbMark", "#7A5A3D", "#4A5681"),
            ("BadgeSurface", "#E3CBAA", "#283556"),
            ("SunColor", "#DFAD50", "#FFE1A8"),
            ("SunriseColor", "#8A673C", "#EBC18A"),
            ("SunsetColor", "#936458", "#C4B5E6"),
            ("ErrorColor", "#A7483B", "#FFB5A7")
        };
        foreach (var (key, day, dark) in palette)
        {
            var color = (Color)ColorConverter.ConvertFromString(night ? dark : day);
            // Swap foregrounds and surfaces together: interpolating light/dark palettes washes out text.
            Resources[key] = new SolidColorBrush(color);
        }
        ApplyBrightnessGradient(night);
        Resources["PrimarySurface"] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(night ? "#23324F" : "#F2D7B7"),
            (Color)ColorConverter.ConvertFromString(night ? "#1D2944" : "#E7BC92"), new Point(0, 0), new Point(1, 1));
        Resources["ContrastFill"] = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(night ? "#D4DBFF" : "#D98C4D"),
            (Color)ColorConverter.ConvertFromString(night ? "#B7C3EC" : "#B75C31"), new Point(0, 0), new Point(0, 1));
        var saturation = (Color)ColorConverter.ConvertFromString(night ? "#70BD88" : "#3C9158");
        Resources["SaturationColor"] = saturation;
        byte grey = (byte)Math.Round(0.2126 * saturation.R + 0.7152 * saturation.G + 0.0722 * saturation.B);
        Resources["SaturationTrack"] = new LinearGradientBrush(Color.FromRgb(grey, grey, grey), saturation, new Point(0, 0), new Point(1, 0));
        DayAtmosphere.Opacity = night ? 0 : 1;
        NightAtmosphere.Opacity = night ? 1 : 0;
        AnimateEmblem(SunEmblem, SunRise, !night, motion);
        AnimateEmblem(MoonEmblem, MoonRise, night, motion);
        AnimateGlow(DayGlowSurface, new Point(0.12, 0.08), !night, motion);
        AnimateGlow(NightGlowSurface, new Point(0.68, 0.03), night, motion);
        UpdateStarMotion();
        UpdateSunRayMotion();
    }

    private void ApplyBrightnessGradient(bool night)
    {
        Color top = (Color)ColorConverter.ConvertFromString(night ? "#BBC5FF" : "#E5BE76");
        Color bottom = (Color)ColorConverter.ConvertFromString(night ? "#BBC5FF" : "#D99D73");
        Resources["BrightnessFill"] = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
    }

    private static void AnimateEmblem(UIElement emblem, TranslateTransform rise, bool entering, bool animate)
    {
        double opacity = emblem.Opacity, y = rise.Y;
        emblem.BeginAnimation(OpacityProperty, null);
        rise.BeginAnimation(TranslateTransform.YProperty, null);
        emblem.Opacity = entering ? 1 : 0;
        rise.Y = entering ? 0 : 38;
        if (!animate) return;
        rise.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(y, rise.Y,
            TimeSpan.FromMilliseconds(entering ? 850 : 400))
        { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = entering ? EasingMode.EaseOut : EasingMode.EaseIn } });
        emblem.BeginAnimation(OpacityProperty, new DoubleAnimation(opacity, emblem.Opacity,
            TimeSpan.FromMilliseconds(entering ? 650 : 300))
        { FillBehavior = FillBehavior.Stop, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
    }

    private static void AnimateGlow(System.Windows.Controls.Border surface, Point center, bool entering, bool animate)
    {
        // Keep the painted surface fixed so its rectangular edge can never enter the window.
        var glow = ((RadialGradientBrush)surface.Background).CloneCurrentValue();
        Point previous = glow.Center;
        Point target = entering ? center : new Point(center.X, center.Y + 0.16);
        glow.Center = glow.GradientOrigin = target;
        if (animate)
        {
            var rise = new PointAnimation(previous, target, TimeSpan.FromMilliseconds(1100))
            { FillBehavior = FillBehavior.Stop, EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
            glow.BeginAnimation(RadialGradientBrush.CenterProperty, rise);
            glow.BeginAnimation(RadialGradientBrush.GradientOriginProperty, rise);
        }
        surface.Background = glow;
    }

    private async Task RenderThemeTransitionAsync(string directory, string name)
    {
        foreach (var (delay, frame) in new[] { (120, "early"), (180, "middle"), (250, "late") })
        {
            await Task.Delay(delay);
            await RenderAsync(Path.Combine(directory, $"transition-{name}-{frame}.png"));
        }
        await Task.Delay(650);
    }
}
