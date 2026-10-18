using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace SolarDesk;

public partial class MainWindow
{
    private void MonitorViewportChanged(object sender, SizeChangedEventArgs e) => UpdateCardWidths();

    private void UpdateCardWidths()
    {
        if (MonitorScroll is null || monitors.Count == 0) return;
        double available = Math.Max(0, MonitorScroll.ActualWidth - monitors.Count * 12);
        double weight = monitors.Sum(m => m.IsPrimary ? 1.12 : 1);
        double unit = Math.Min(330, Math.Max(224, available / weight));
        foreach (var monitor in monitors) monitor.CardWidth = Math.Floor(unit * (monitor.IsPrimary ? 1.12 : 1));
    }

    private async Task CheckNewDesignAsync(Action<bool, string> check, string directory)
    {
        UpdateCardWidths(); UpdateLayout();
        var sliders = VisualChildren<Slider>(MonitorItems).ToList();
        check(sliders.Count(s => s.Orientation == Orientation.Vertical) == 9,
            "Each display has three vertical bars for brightness, contrast and saturation");
        var saturation = sliders.First(s => s.DataContext == monitors[0] && AutomationProperties.GetName(s) == "Saturation");
        int saturationBefore = monitors[0].SelectedSaturation;
        Slider.IncreaseSmall.Execute(null, saturation);
        check(monitors[0].SelectedSaturation == saturationBefore + 1, "The third vertical bar supports keyboard saturation adjustment");
        saturation.SetCurrentValue(Slider.ValueProperty, (double)saturationBefore);
        await FlushSliderEditsAsync();
        var contrast = sliders.First(s => s.DataContext == monitors[0] && AutomationProperties.GetName(s) == "Contrast");
        int before = monitors[0].SelectedContrast;
        Slider.IncreaseSmall.Execute(null, contrast);
        check(monitors[0].SelectedContrast == before + 1, "Vertical contrast supports keyboard adjustment");
        contrast.SetCurrentValue(Slider.ValueProperty, (double)before);
        await FlushSliderEditsAsync();
        check(monitors[1].CardWidth > monitors[0].CardWidth && monitors[1].ScreenWidth > monitors[0].ScreenWidth
            && monitors[1].AspectText == "21:9" && monitors[0].AspectText == "16:9", "Main ultrawide card and icon are larger and resolution labels reflect each display");
        check(SunriseText.TranslatePoint(new Point(), this).Y < MonitorItems.TranslatePoint(new Point(), this).Y,
            "Sunrise, sunset and weather appear above monitor controls");
        fetchWeather = (_, _, _) => Task.FromResult(new WeatherSnapshot(8, 63, true, DateTimeOffset.UtcNow));
        await RefreshWeatherAsync(force: true);
        await RenderAsync(Path.Combine(directory, "weather-rain.png"));
        check(WeatherText.Text == "Rain" && TemperatureText.Text.Contains("8"), "Weather updates temperature and condition from the response");
        fetchWeather = (_, _, _) => Task.FromResult(new WeatherSnapshot(-2, 73, false, DateTimeOffset.UtcNow));
        await RefreshWeatherAsync(force: true);
        await RenderAsync(Path.Combine(directory, "weather-snow.png"));
        fetchWeather = (_, _, _) => Task.FromException<WeatherSnapshot>(new System.Net.Http.HttpRequestException("Offline test"));
        weather = null;
        await RefreshWeatherAsync(force: true);
        check(WeatherText.Text == "Weather unavailable" && sliders.All(s => s.IsEnabled), "Weather failure leaves all monitor controls usable");
        var pending = new TaskCompletionSource<WeatherSnapshot>();
        fetchWeather = (_, _, _) => pending.Task;
        var oldRequest = RefreshWeatherAsync(force: true);
        ResetWeather();
        fetchWeather = (_, _, _) => Task.FromResult(new WeatherSnapshot(14, 2, true, DateTimeOffset.UtcNow));
        // Reset again so the just-started stale request is also canceled.
        ResetWeather();
        pending.SetResult(new WeatherSnapshot(99, 63, true, DateTimeOffset.UtcNow));
        await oldRequest;
        await RefreshWeatherAsync(force: true);
        check(weather?.Celsius == 14, "Canceled weather requests cannot overwrite a newer location");
        double width = Width;
        Width = MinWidth;
        await RenderAsync(Path.Combine(directory, "paired-small.png"));
        check(ContextHeader.ActualWidth > 0 && SunriseText.TranslatePoint(new Point(), ContextHeader).X > WeatherPanel.TranslatePoint(new Point(), ContextHeader).X
            && LocationChip.ActualWidth > 100, "Top context remains separated at minimum window width");
        Width = width;
        await RenderAsync(Path.Combine(directory, "paired-day.png"));
    }
}
