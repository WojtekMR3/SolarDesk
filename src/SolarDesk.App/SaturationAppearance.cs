using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SolarDesk;

public static class SaturationAppearance
{
    public static readonly DependencyProperty LevelProperty = DependencyProperty.RegisterAttached(
        "Level", typeof(double), typeof(SaturationAppearance), new PropertyMetadata(50d, AppearanceChanged));
    public static readonly DependencyProperty ColorProperty = DependencyProperty.RegisterAttached(
        "Color", typeof(Color), typeof(SaturationAppearance), new PropertyMetadata(Colors.Transparent, AppearanceChanged));

    public static double GetLevel(DependencyObject target) => (double)target.GetValue(LevelProperty);
    public static void SetLevel(DependencyObject target, double value) => target.SetValue(LevelProperty, value);
    public static Color GetColor(DependencyObject target) => (Color)target.GetValue(ColorProperty);
    public static void SetColor(DependencyObject target, Color value) => target.SetValue(ColorProperty, value);

    private static void AppearanceChanged(DependencyObject target, DependencyPropertyChangedEventArgs e)
    {
        if (target is not Slider slider) return;
        Color color = GetColor(slider);
        double amount = Math.Clamp(GetLevel(slider), 0, 100) / 100;
        double grey = 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
        byte Channel(byte channel) => (byte)Math.Round(grey + (channel - grey) * amount);
        var top = Color.FromRgb(Channel(color.R), Channel(color.G), Channel(color.B));
        var bottom = Color.FromRgb((byte)Math.Round(top.R * 0.9), (byte)Math.Round(top.G * 0.9), (byte)Math.Round(top.B * 0.9));
        var fill = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
        var thumb = new SolidColorBrush(top);
        fill.Freeze(); thumb.Freeze();
        slider.Foreground = fill;
        slider.Background = thumb;
    }
}
