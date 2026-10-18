using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace SolarDesk;

public partial class MainWindow
{
    private static Color SaturationThumbColor(Slider slider) => ((SolidColorBrush)slider.Background).Color;
    private static int ColorSpread(Color color) => Math.Max(color.R, Math.Max(color.G, color.B)) - Math.Min(color.R, Math.Min(color.G, color.B));

    private async Task CheckSaturationAppearanceAsync(Action<bool, string> check, string directory)
    {
        if (!diagnostic) throw new InvalidOperationException("Saturation appearance checks require demo mode.");
        bool originalNight = selectedNightProfile;
        var saved = monitors.Select(m => DisplayProfileValues.Capture(m.Profile)).ToArray();
        foreach (bool night in new[] { false, true })
        {
            if (night) NightTab.IsChecked = true;
            else DayTab.IsChecked = true;
            await Task.Delay(1200);
            UpdateLayout();
            var sliders = monitors.Select(m => VisualChildren<Slider>(MonitorItems)
                .First(s => s.DataContext == m && AutomationProperties.GetName(s) == "Saturation")).ToArray();
            var samples = new List<Color>();
            foreach (int level in new[] { 0, 25, 50, 75, 100 })
            {
                sliders[0].SetCurrentValue(Slider.ValueProperty, (double)level);
                UpdateLayout();
                samples.Add(SaturationThumbColor(sliders[0]));
            }
            check(ColorSpread(samples[0]) == 0 && samples.Zip(samples.Skip(1), (a, b) => ColorSpread(a) < ColorSpread(b)).All(increasing => increasing),
                $"{(night ? "Night" : "Day")} saturation changes live from grey at 0 to progressively richer color at 100");
            check(((LinearGradientBrush)sliders[0].Foreground).GradientStops[0].Color == samples[^1],
                $"{(night ? "Night" : "Day")} saturation fill and handle share the same intensity");
            for (int i = 0; i < sliders.Length; i++) sliders[i].SetCurrentValue(Slider.ValueProperty, i * 50d);
            await FlushSliderEditsAsync();
            await RenderAsync(Path.Combine(directory, night ? "saturation-levels-night.png" : "saturation-levels-day.png"));
        }
        for (int i = 0; i < monitors.Count; i++)
        {
            monitors[i].DaySaturation = saved[i].Day.Saturation;
            monitors[i].NightSaturation = saved[i].Night.Saturation;
        }
        if (originalNight) NightTab.IsChecked = true;
        else DayTab.IsChecked = true;
        await FlushSliderEditsAsync();
        await Task.Delay(1200);
        check(monitors.Select((m, i) => DisplayProfileValues.Capture(m.Profile) == saved[i]).All(same => same),
            "Saturation appearance checks restore all profile values and preserve brightness and contrast");
    }
}
