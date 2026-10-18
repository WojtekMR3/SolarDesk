using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace SolarDesk;

public partial class MainWindow
{
    private CancellationTokenSource? weatherCancellation;
    private WeatherSnapshot? weather;
    private DateTimeOffset nextWeatherRefresh;
    private Func<double, double, CancellationToken, Task<WeatherSnapshot>> fetchWeather = WeatherService.FetchAsync;

    private void InitializeWeather()
    {
        WeatherCheck.IsChecked = settings.WeatherEnabled;
        WeatherEffectsCheck.IsChecked = settings.WeatherEffectsEnabled;
        if (diagnostic) fetchWeather = (_, _, _) => Task.FromResult(new WeatherSnapshot(14, 2, true, DateTimeOffset.UtcNow));
        IsVisibleChanged += (_, _) => { UpdateWeatherEffects(); if (IsVisible) _ = RefreshWeatherAsync(); };
        StateChanged += (_, _) => UpdateWeatherEffects();
    }

    private void ResetWeather()
    {
        weatherCancellation?.Cancel();
        weatherCancellation = null;
        weather = null;
        nextWeatherRefresh = DateTimeOffset.MinValue;
        ShowWeather();
        if (ready) _ = RefreshWeatherAsync();
    }

    private async Task RefreshWeatherAsync(bool force = false)
    {
        if (!ready || exiting || !settings.HasLocation || !settings.WeatherEnabled || (!IsVisible && !force)) return;
        if (weather is not null && !weather.IsFresh(DateTimeOffset.UtcNow)) { weather = null; ShowWeather(unavailable: true); }
        if (weatherCancellation is not null || (!force && DateTimeOffset.UtcNow < nextWeatherRefresh)) return;
        var request = new CancellationTokenSource();
        weatherCancellation = request;
        nextWeatherRefresh = DateTimeOffset.UtcNow.AddMinutes(15);
        try
        {
            var result = await fetchWeather(settings.Latitude!.Value, settings.Longitude!.Value, request.Token);
            if (request.IsCancellationRequested || exiting || weatherCancellation != request) return;
            if (!result.IsFresh(DateTimeOffset.UtcNow)) throw new JsonException("Weather data is out of date.");
            weather = result;
            ShowWeather();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException)
        {
            if (!request.IsCancellationRequested && !exiting && weatherCancellation == request)
            {
                nextWeatherRefresh = DateTimeOffset.UtcNow.AddMinutes(5);
                ShowWeather(unavailable: true);
            }
        }
        finally
        {
            if (weatherCancellation == request) weatherCancellation = null;
            request.Dispose();
        }
    }

    private void ShowWeather(bool unavailable = false)
    {
        WeatherEffectsCheck.IsEnabled = settings.WeatherEnabled;
        TemperatureText.Text = weather is null ? "— °C" : weather.Celsius.ToString("0", CultureInfo.CurrentCulture) + " °C";
        WeatherText.Text = !settings.WeatherEnabled ? "Weather off" : !settings.HasLocation ? "Choose a city"
            : weather is not null ? weather.Description + (unavailable ? " · cached" : "")
            : unavailable ? "Weather unavailable" : "Loading weather";
        WeatherPanel.ToolTip = weather is null ? "Weather by Open-Meteo · requires an internet connection"
            : $"{weather.Description} · {weather.Celsius:0.#} °C\nUpdated {TimeZoneInfo.ConvertTime(weather.At, TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId)):HH:mm} · Open-Meteo\nApproximate local conditions";
        const string cloud = "M5,17 C0,17 0,10 5,10 C5,3 15,2 17,9 C24,8 25,17 19,17 Z";
        string icon = weather is null ? "M3,12 L21,12" : weather.Effect switch
        {
            WeatherEffect.Rain => cloud + " M7,21 L5,25 M14,21 L12,25 M21,21 L19,25",
            WeatherEffect.Snow => cloud + " M9,21 L9,27 M6,24 L12,24 M7,22 L11,26 M11,22 L7,26 M20,22 L20,26 M18,24 L22,24",
            _ when weather.Code is 0 or 1 && weather.IsDay => "M12,6 A6,6 0 1 1 11.99,6 M12,0 L12,3 M12,21 L12,24 M0,12 L3,12 M21,12 L24,12 M3,3 L5,5 M19,19 L21,21 M3,21 L5,19 M19,5 L21,3",
            _ when weather.Code is 0 or 1 => "M22,16 A10,10 0 0 1 12,4 A10,10 0 1 0 22,16 Z",
            _ when weather.Code is 45 or 48 => "M3,6 L21,6 M0,12 L18,12 M5,18 L24,18",
            _ => cloud
        };
        WeatherIcon.Data = Geometry.Parse(icon);
        UpdateWeatherEffects();
    }

    private void WeatherSettingClick(object sender, RoutedEventArgs e)
    {
        settings.WeatherEnabled = WeatherCheck.IsChecked == true;
        settings.WeatherEffectsEnabled = WeatherEffectsCheck.IsChecked == true;
        SaveSettings();
        ResetWeather();
    }

    private void WeatherCreditClick(object sender, RoutedEventArgs e)
    {
        if (!diagnostic) Process.Start(new ProcessStartInfo("https://open-meteo.com/") { UseShellExecute = true });
    }

    private void UpdateWeatherEffects()
    {
        foreach (UIElement child in WeatherParticles.Children)
            if (child.RenderTransform is TranslateTransform motion) motion.BeginAnimation(TranslateTransform.YProperty, null);
        WeatherParticles.Children.Clear();
        if (weather is null || !weather.IsFresh(DateTimeOffset.UtcNow) || !settings.WeatherEnabled || !settings.WeatherEffectsEnabled
            || !IsVisible || exiting || WindowState == WindowState.Minimized || !SystemParameters.ClientAreaAnimation) return;
        if (weather.Effect == WeatherEffect.None) return;
        bool snow = weather.Effect == WeatherEffect.Snow;
        var random = new Random(42);
        for (int i = 0; i < 22; i++)
        {
            Shape particle = snow ? new Ellipse { Width = 3, Height = 3 }
                : new Line { X1 = 4, X2 = 0, Y1 = 0, Y2 = 11, StrokeThickness = 1.2 };
            particle.SetResourceReference(Shape.FillProperty, "Muted");
            particle.SetResourceReference(Shape.StrokeProperty, "Muted");
            particle.Opacity = snow ? 0.55 : 0.35;
            Canvas.SetLeft(particle, random.NextDouble() * 1000);
            Canvas.SetTop(particle, -12);
            var motion = new TranslateTransform();
            particle.RenderTransform = motion;
            var fall = new DoubleAnimation(0, 94, TimeSpan.FromSeconds(snow ? 5 + random.NextDouble() * 3 : 1.3 + random.NextDouble()))
            { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(-random.NextDouble() * 7) };
            Timeline.SetDesiredFrameRate(fall, 20);
            motion.BeginAnimation(TranslateTransform.YProperty, fall);
            WeatherParticles.Children.Add(particle);
        }
    }
}
