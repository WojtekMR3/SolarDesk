using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace SolarDesk;

public partial class MainWindow : Window
{
    private readonly bool diagnostic;
    private readonly MonitorService hardware = new();
    private readonly SolarSchedule schedule = new();
    private readonly ObservableCollection<MonitorViewModel> monitors = [];
    private readonly SettingsStore store;
    private readonly AppSettings settings;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(10) };
    private readonly DispatcherTimer saveTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private bool ready, busy, exiting, refreshRequested = true, saveFailed;
    private bool selectedNightProfile, applyProfileOnStartup = true, locationBusy, enableAfterLocation, startupPromptConsidered;
    private bool updatingProfileSelection, cycleRequested, forceRequestedCycle;
    private DateTimeOffset lastScan = DateTimeOffset.MinValue;
    private DateTimeOffset lastTrayRefresh = DateTimeOffset.MinValue;
    private bool scanningDisplays;
    private Func<Task<List<MonitorReading>>>? diagnosticScan;
    private DateTimeOffset? manualUntil, pauseUntil;
    private double? manualDayAmount;
    private readonly Dictionary<string, HashSet<string>> pendingSliderEdits = [];
    private Func<Task<DetectedLocation>> detectLocation = LocationService.DetectAsync;

    public MainWindow(bool diagnostic = false, AppSettings? diagnosticSettings = null)
    {
        this.diagnostic = diagnostic;
        store = new SettingsStore(Path.Combine(App.DataDirectory, "settings.json"));
        settings = diagnostic ? diagnosticSettings ?? new AppSettings
        {
            ManualProfileSelected = false, LocationName = "Warsaw", Latitude = 52.2297, Longitude = 21.0122,
            TimeZoneId = "Central European Standard Time"
        } : store.Load();
        InitializeComponent();
        selectedNightProfile = settings.SelectedNightProfile;
        settings.ManualProfileSelected = false;
        NightTab.IsChecked = selectedNightProfile;
        DayTab.IsChecked = !selectedNightProfile;
        // Retire the independent appearance override from the former Manual mode.
        settings.Appearance = AppAppearance.FollowProfile;
        InitializeStarMotion();
        InitializeWeather();
        InitializeSunFeatures();
        InitializeBackgroundActivity();
        ApplyProfileTheme(animate: false);
        Height = Math.Min(860, SystemParameters.WorkArea.Height - 40);
        Width = Math.Min(1000, SystemParameters.WorkArea.Width - 40);
        MonitorItems.ItemsSource = monitors;
        monitors.CollectionChanged += (_, _) => UpdateCardWidths();
        LinkModeBox.SelectedIndex = (int)settings.LinkMode;
        LinkModeBox.ToolTip = ControlHint;
        Message(ControlHint);
        AutoMonitorItems.ItemsSource = monitors;
        SharedAdditionalItems.ItemsSource = monitors;
        ZoneBox.ItemsSource = TimeZoneInfo.GetSystemTimeZones();
        PopulateLocation();
        FadeSlider.Value = settings.TransitionMinutes;
        FadeLabel.Text = settings.TransitionMinutes + " min";
        RiseOffsetBox.Text = settings.SunriseOffsetMinutes.ToString(CultureInfo.InvariantCulture);
        SetOffsetBox.Text = settings.SunsetOffsetMinutes.ToString(CultureInfo.InvariantCulture);
        StartupCheck.IsChecked = !diagnostic && IsStartupEnabled();
        timer.Tick += async (_, _) => await CycleAsync();
        saveTimer.Tick += async (_, _) => await FlushSliderEditsAsync();
        Closing += OnClosing;
        SizeChanged += (_, _) =>
        {
            LocationCard.MaxHeight = Math.Max(300, ActualHeight - 48);
            SettingsCard.MaxHeight = Math.Min(740, Math.Max(300, ActualHeight - 48));
            UpdateScheduleDisplay();
            UpdateCardWidths();
        };
    }

    public async Task InitializeAsync()
    {
        ready = true;
        if (!diagnostic)
        {
            CreateTray();
            SystemEvents.DisplaySettingsChanged += DisplaysChanged;
            SystemEvents.PowerModeChanged += PowerChanged;
            SystemEvents.TimeChanged += TimeChanged;
        }
        await CycleAsync();
        if (store.RecoveryMessage is not null) Message(store.RecoveryMessage);
        if (!diagnostic) timer.Start();
        InitializePresetHotkeys();
        _ = RefreshWeatherAsync();
    }

    private void PopulateLocation()
    {
        // Replace legacy coordinate/generic labels locally; never geocode over a network.
        if (settings.HasLocation && (settings.LocationName.StartsWith("Windows location", StringComparison.Ordinal)
            || settings.LocationName.Contains('°') || settings.LocationName == "Saved location"))
            settings.LocationName = CityCatalog.Label(settings.Latitude!.Value, settings.Longitude!.Value);
        LocationText.Text = settings.HasLocation ? settings.LocationName : "Set location";
        LatitudeBox.Text = settings.Latitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "";
        LongitudeBox.Text = settings.Longitude?.ToString("0.#####", CultureInfo.InvariantCulture) ?? "";
        ZoneBox.SelectedValue = settings.TimeZoneId;
        LocationChip.ToolTip = settings.HasLocation ? "Change location · " + settings.LocationName : "Set location for sunrise and sunset";
        ResetWeather();
    }

    private async Task CycleAsync(bool force = false)
    {
        if (!ready || exiting) return;
        if (busy) { cycleRequested = true; forceRequestedCycle |= force; return; }
        busy = true;
        if (diagnostic) diagnosticCycleCount++;
        try
        {
            var now = CurrentTime;
            if (manualDayAmount.HasValue && settings.AutoEnabled && !manualUntil.HasValue)
                manualUntil = NextAutomaticTransition();
            if (manualUntil <= now) { controlRevision++; ClearManualPreview(); }
            if (pauseUntil <= now) pauseUntil = null;
            if (refreshRequested || (PresentationActive && now - lastScan >= TimeSpan.FromMinutes(5)))
            {
                refreshRequested = false;
                if (diagnostic) diagnosticScanCount++;
                RefreshButton.IsEnabled = false;
                scanningDisplays = true;
                UpdateTrayControls();
                var readings = diagnostic ? await (diagnosticScan?.Invoke() ?? Task.FromResult(DemoReadings())) : await hardware.ScanAsync();
                // Keep monitor columns in the same left-to-right, then top-to-bottom order as Windows' desktop layout.
                readings = readings.OrderBy(reading => reading.Left).ThenBy(reading => reading.Top).ToList();
                if (exiting) return;
                lastScan = now;
                scanningDisplays = false;
                monitors.Clear();
                foreach (var reading in readings)
                {
                    if (!settings.Monitors.TryGetValue(reading.Id, out var profile))
                    {
                        profile = new MonitorProfile
                        {
                            DayBrightness = reading.Brightness.HasValue ? reading.BrightnessPercent : 65,
                            NightBrightness = reading.Brightness.HasValue ? Math.Min(25, reading.BrightnessPercent) : 25,
                            DayContrast = reading.Contrast.HasValue ? reading.ContrastPercent : 50,
                            NightContrast = reading.Contrast.HasValue ? reading.ContrastPercent : 50,
                            DaySaturation = reading.Saturation.HasValue ? reading.SaturationPercent : 50,
                            NightSaturation = reading.Saturation.HasValue ? reading.SaturationPercent : 50,
            Enabled = reading.Brightness.HasValue || reading.Contrast.HasValue || reading.Saturation.HasValue || reading.Advanced?.Count > 0
                        };
                        settings.Monitors[reading.Id] = profile;
                    }
                    // Record the first successfully read raw values, including a later successful DDC retry.
                    profile.OriginalBrightnessRaw ??= reading.Brightness;
                    profile.OriginalContrastRaw ??= reading.Contrast;
                    MigrateLegacyColorPreset(profile, reading);
                    profile.OriginalColorPresetRaw ??= reading.ColorPresetRaw;
                    profile.CaptureSaturation(reading.SaturationSource, reading.SaturationPercent,
                        reading.SaturationSource == "ddc" ? reading.Saturation : null, reading.NvidiaVibranceRaw);
                    profile.CaptureAdvanced(reading.Advanced ?? []);
                    string position = readings.Count == 3 ? new[] { "Left", "Center", "Right" }[monitors.Count]
                        : readings.Count == 2 ? new[] { "Left", "Right" }[monitors.Count] : "Display " + (monitors.Count + 1);
                    monitors.Add(new MonitorViewModel(reading, profile, ProfileChanged)
                        { IsNightProfile = selectedNightProfile, PositionLabel = position });
                }
                if (!diagnostic && settings.Monitors.Values.Any(profile => profile.ColorPresetRestorePending))
                {
                    var restoredPresets = await hardware.RestoreLegacyColorPresetsAsync(settings);
                    if (restoredPresets.Any(result => !result.Success))
                        Message("Could not restore a previous color preset. Check the monitor's DDC/CI setting.");
                }
                DisplayCountText.Text = monitors.Count == 1 ? "1 display connected" : $"{monitors.Count} displays · arranged like your desk";
                if (applyProfileOnStartup && monitors.Count > 0)
                {
                    applyProfileOnStartup = false;
                    if (!settings.AutoEnabled)
                    {
                        manualDayAmount = selectedNightProfile ? 0 : 1;
                        previewWholeProfile = previewIncludesAllDisplays = true;
                    }
                }
                UpdateLinkedControls();
                EmptyText.Visibility = monitors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
                EmptyText.Text = "No displays found. Connect a monitor, then refresh in Settings.";
                SaveSettings();
                RefreshButton.IsEnabled = true;
            }
            UpdateScheduleDisplay();
            _ = RefreshWeatherAsync();
            CheckSunReminders(now);
            double? amount = manualDayAmount;
            if (pauseUntil.HasValue) amount = null;
            else if (!amount.HasValue && settings.AutoEnabled && settings.HasLocation)
            {
                amount = schedule.Evaluate(now, settings).DayAmount;
                if (selectedNightProfile != (amount.Value < 0.5))
                {
                    SelectProfileEditor(amount.Value < 0.5);
                    SaveSettings();
                }
            }
            if (amount.HasValue && !saveFailed && !exiting && pendingSliderEdits.Count == 0)
            {
                var targets = PreviewTargets(amount.Value);
                long revision = controlRevision;
                var results = await WriteTargetsAsync(targets, force);
                if (exiting || revision != controlRevision) return;
                foreach (var result in results)
                {
                    var monitor = monitors.First(m => m.Id == result.Id);
                    monitor.UpdateAdvancedReading(result.Advanced);
                    var target = targets.First(t => t.Id == result.Id);
                    if (result.Success) monitor.UpdateCurrent(target.Brightness);
                    monitor.Status = result.Success ? (diagnostic ? "Demo controls · hardware untouched" : monitor.Reading.Status) : result.Message;
                }
                if (results.Any(r => !r.Success)) Message("A monitor did not accept a setting. See its status below; Sunset Monitor will retry.");
            }
        }
        catch (Exception ex) { ReportError(ex); }
        finally
        {
            busy = false; scanningDisplays = false; RefreshButton.IsEnabled = true;
            UpdateTrayControls();
            if (cycleRequested && !exiting)
            {
                bool queuedForce = forceRequestedCycle;
                cycleRequested = forceRequestedCycle = false;
                _ = Dispatcher.BeginInvoke(async () => await CycleAsync(queuedForce));
            }
        }
    }

    private static string Percent(int? value) => value.HasValue ? value + "%" : "unchanged";
    private static bool MigrateLegacyColorPreset(MonitorProfile profile, MonitorReading reading)
    {
        if (profile.SaturationSource is not null || profile.OriginalColorPresetRaw.HasValue
            || profile.OriginalSaturationRaw is not uint legacyPreset) return false;
        profile.OriginalColorPresetRaw = legacyPreset;
        profile.ColorPresetRestorePending = true;
        profile.OriginalSaturationRaw = reading.SaturationSource == "ddc" ? reading.Saturation : null;
        if (reading.Saturation.HasValue)
        {
            profile.DaySaturation = reading.SaturationPercent;
            profile.NightSaturation = reading.SaturationPercent;
        }
        profile.SaturationSource = reading.SaturationSource;
        return true;
    }

    private static List<MonitorReading> DemoReadings() =>
        [new("demo-left", "BenQ GL2250 · Display 2", "DISPLAY2", 55, 100, 40, 100, 50, 100, false, "Ready", -1920, 0, "nvidia", 50, 1920, 1080),
         new("demo-main", "MPG341CX OLED · Display 1", "DISPLAY1", 60, 100, 55, 100, 50, 100, true, "Ready", 0, 0, "ddc", null, 3440, 1440, DemoAdvancedControls()),
         new("demo-right", "XB253Q · Display 3", "DISPLAY3", 40, 100, 45, 100, 50, 100, false, "Ready", 3440, 0, "nvidia", 50, 1920, 1080)];

    private void UpdateScheduleDisplay()
    {
        if (!ready) return;
        var now = CurrentTime;
        ScheduleState? state = settings.HasLocation ? schedule.Evaluate(now, settings) : null;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(settings.TimeZoneId);
        SunriseText.Text = state is null ? "—" : FormatSunEvent(state.Today.Events.FirstOrDefault(e => e.IsSunrise), zone);
        SunsetText.Text = state is null ? "—" : FormatSunEvent(state.Today.Events.FirstOrDefault(e => !e.IsSunrise), zone);
        AutomationButton.IsChecked = settings.AutoEnabled;
        ResumeButton.Visibility = settings.AutoEnabled && (manualDayAmount.HasValue || pauseUntil.HasValue) ? Visibility.Visible : Visibility.Collapsed;
        if (state is not null)
        {
            PhaseText.Text = settings.AutoEnabled ? "Following the sun" : SelectedProfileName + " settings";
            string fade = settings.TransitionMinutes == 0 ? "instant switch" : $"{settings.TransitionMinutes}-min fade";
            NextEventText.Text = state.NextEvent is null ? (state.DayAmount > 0 ? "Daylight all day" : "Night all day")
                : $"{(state.NextEvent.IsSunrise ? "Day" : "Night")} at {TimeZoneInfo.ConvertTime(state.NextEvent.At, zone):HH:mm} · {fade}";
        }
        else
        {
            PhaseText.Text = "Follow the sun";
            NextEventText.Text = "Set a location for automatic day and night settings.";
        }
        if (manualDayAmount.HasValue)
        {
            PhaseText.Text = SelectedProfileName + " settings active";
            NextEventText.Text = settings.AutoEnabled && manualUntil.HasValue ? $"Auto on · next transition {TimeZoneInfo.ConvertTime(manualUntil.Value, zone):HH:mm}"
                : settings.AutoEnabled ? "Auto on · waiting for the next sun transition" : "Changes saved · auto is off";
        }
        else if (pauseUntil.HasValue)
        {
            PhaseText.Text = "Auto is paused";
            NextEventText.Text = $"Resumes at {pauseUntil.Value.ToLocalTime():HH:mm}";
        }
        if (!settings.AutoEnabled && !pauseUntil.HasValue)
        {
            PhaseText.Text = SelectedProfileName + " settings";
            NextEventText.Text = "Auto off · stays on " + SelectedProfileName + " until you switch";
        }
        ResumeButton.Content = pauseUntil.HasValue ? "Resume auto" : "Follow sun now";
        if (tray is not null)
        {
            string tooltip = App.DisplayName + " · " + PhaseText.Text;
            tray.Text = tooltip[..Math.Min(63, tooltip.Length)];
        }
        UpdateTrayControls();
        UpdateCycleInterval();
    }

    private async void ProfileSelected(object sender, RoutedEventArgs e)
    {
        if (!ready || updatingProfileSelection) return;
        await ApplySelectedProfileAsync(NightTab.IsChecked == true);
    }

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        SettingsLayer.Visibility = Visibility.Visible;
        MainSurface.IsEnabled = false;
        CloseSettingsButton.Focus();
    }

    private void CloseSettingsClick(object sender, RoutedEventArgs e) => CloseSettings();
    private void CloseSettings()
    {
        SettingsLayer.Visibility = Visibility.Collapsed;
        MainSurface.IsEnabled = true;
        DayTab.Focus();
    }

    public void PromptLocationOnFirstLaunch(bool launchedInTray)
    {
        if (startupPromptConsidered || launchedInTray) return;
        startupPromptConsidered = true;
        if (!settings.HasLocation && !settings.LocationPromptDismissed) ShowLocation();
    }

    private void LocationClick(object sender, RoutedEventArgs e) => ShowLocation();
    private void ShowLocation(bool enableAuto = false)
    {
        enableAfterLocation = enableAuto;
        LocationHelpText.Visibility = Visibility.Collapsed;
        LocationPermissionButton.Visibility = Visibility.Collapsed;
        LocationLayer.Visibility = Visibility.Visible;
        MainSurface.IsEnabled = false;
        SettingsLayer.IsEnabled = false;
        SkipLocationButton.Content = settings.HasLocation ? "Keep current location" : "Not now";
        DetectButton.Focus();
    }

    private void SkipLocationClick(object sender, RoutedEventArgs e) => DismissLocation();
    private void DismissLocation()
    {
        if (locationBusy) return;
        settings.LocationPromptDismissed = true;
        SaveSettings();
        CloseLocation();
    }

    private void CloseLocation()
    {
        enableAfterLocation = false;
        LocationLayer.Visibility = Visibility.Collapsed;
        MainSurface.IsEnabled = SettingsLayer.Visibility != Visibility.Visible;
        SettingsLayer.IsEnabled = true;
        LocationChip.Focus();
    }

    private void WindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        if (LocationLayer.Visibility == Visibility.Visible) { DismissLocation(); e.Handled = true; }
        else if (SettingsLayer.Visibility == Visibility.Visible) { CloseSettings(); e.Handled = true; }
    }

    private void CitySearchChanged(object sender, TextChangedEventArgs e)
    {
        if (CityResults is null || locationBusy) return;
        CityResults.ItemsSource = CityCatalog.Search(CitySearchBox.Text);
    }

    private async void CityClicked(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source && ItemsControl.ContainerFromElement(CityResults, source) is ListBoxItem item
            && item.DataContext is City city) await ChooseCityAsync(city);
    }

    private async void CityKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && CityResults.SelectedItem is City city)
        { e.Handled = true; await ChooseCityAsync(city); }
    }

    private async Task ChooseCityAsync(City city)
    {
        if (locationBusy) return;
        try { await SaveDetectedLocationAsync(new(city.Name, city.Latitude, city.Longitude, city.WindowsTimeZoneId)); }
        catch (Exception ex) { ReportError(ex); }
    }

    private async void ResumeClick(object sender, RoutedEventArgs e) => await ResumeAsync();

    private static string FormatSunEvent(SolarEvent? e, TimeZoneInfo zone) => e is null ? "—" : TimeZoneInfo.ConvertTime(e.At, zone).ToString("HH:mm");
    private void ProfileChanged(MonitorViewModel monitor, string property, int? previousValue = null)
    {
        if (!ready) return;
        if (property is nameof(MonitorViewModel.DayBrightness) or nameof(MonitorViewModel.NightBrightness)
            or nameof(MonitorViewModel.DayContrast) or nameof(MonitorViewModel.NightContrast)
            or nameof(MonitorViewModel.DaySaturation) or nameof(MonitorViewModel.NightSaturation) or "DayAdvanced" or "NightAdvanced")
        {
            if (!property.EndsWith("Advanced", StringComparison.Ordinal) && !applyingSharedValues && settings.LinkMode == MonitorLinkMode.KeepOffsets)
            {
                if (property.StartsWith("Night", StringComparison.Ordinal)) settings.NightOffsets = null;
                else settings.DayOffsets = null;
            }
            if (!pendingSliderEdits.TryGetValue(monitor.Id, out var edits)) pendingSliderEdits[monitor.Id] = edits = [];
            edits.Add(property);
            controlRevision++;
            if (previewWholeProfile) manualEdits.Clear();
            previewWholeProfile = false;
            manualDayAmount = property.StartsWith("Night", StringComparison.Ordinal) ? 0 : 1;
            manualUntil = NextAutomaticTransition();
            pauseUntil = null;
            if (!applyingSharedValues) UpdateSharedControls();
        }
        ScheduleSave();
    }

    private void ScheduleSave()
    {
        if (!ready) return;
        saveTimer.Stop();
        saveTimer.Start();
    }

    private async Task FlushSliderEditsAsync(bool force = false)
    {
        saveTimer.Stop();
        if (exiting || !SaveSettings()) return;
        if (pendingSliderEdits.Count == 0) { await CycleAsync(); return; }

        var edits = pendingSliderEdits.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        pendingSliderEdits.Clear();
        var targets = new List<MonitorTarget>();
        foreach (var (id, properties) in edits)
        {
            var monitor = monitors.FirstOrDefault(item => item.Id == id);
            if (monitor is null) continue;
            int? brightness = null, contrast = null, saturation = null;
            AdvancedValues? advanced = null;
            foreach (var property in properties)
            {
                bool night = property.StartsWith("Night", StringComparison.Ordinal);
                if (property.EndsWith("Advanced", StringComparison.Ordinal))
                {
                    advanced = night ? monitor.Profile.NightAdvanced : monitor.Profile.DayAdvanced;
                    continue;
                }
                int value = property switch
                {
                    nameof(MonitorViewModel.DayBrightness) => monitor.DayBrightness,
                    nameof(MonitorViewModel.NightBrightness) => monitor.NightBrightness,
                    nameof(MonitorViewModel.DayContrast) => monitor.DayContrast,
                    nameof(MonitorViewModel.NightContrast) => monitor.NightContrast,
                    nameof(MonitorViewModel.DaySaturation) => monitor.DaySaturation,
                    nameof(MonitorViewModel.NightSaturation) => monitor.NightSaturation,
                    _ => 0
                };
                if (property.EndsWith("Brightness", StringComparison.Ordinal)) brightness = value;
                else if (property.EndsWith("Contrast", StringComparison.Ordinal)) contrast = value;
                else if (property.EndsWith("Saturation", StringComparison.Ordinal)) saturation = value;
            }
            targets.Add(new MonitorTarget(id, brightness, contrast, saturation, advanced));
        }
        if (targets.Count == 0) return;

        RememberManualEdits(targets);
        long revision = controlRevision;
        var results = await WriteTargetsAsync(targets, force);
        if (exiting || revision != controlRevision) return;
        foreach (var result in results)
        {
            var monitor = monitors.FirstOrDefault(item => item.Id == result.Id);
            if (monitor is null) continue;
            var target = targets.First(item => item.Id == result.Id);
            monitor.UpdateAdvancedReading(result.Advanced);
            if (result.Success)
            {
                monitor.UpdateCurrent(target.Brightness);
                monitor.Status = diagnostic ? "Demo controls · hardware untouched" : monitor.Reading.Status;
                Message(diagnostic ? "Demo slider changed · hardware is untouched." :
                    $"{SelectedProfileName} profile applied to {monitor.Name}.");
            }
            else { monitor.Status = result.Message; Message($"{monitor.Name}: {result.Message}"); }
        }
        if (targets.Count > 1)
        {
            int failed = results.Count(result => !result.Success);
            Message(failed > 0 ? $"{failed} displays could not be updated. See their monitor cards."
                : $"{SelectedProfileName} values updated on {targets.Count} displays.");
        }
        UpdateScheduleDisplay();
    }
    private bool SaveSettings()
    {
        if (diagnostic) { settings.RememberCurrentMode(); return true; }
        try { store.Save(settings); saveFailed = false; return true; }
        catch (Exception ex) { saveFailed = true; settings.AutoEnabled = false; App.Log(ex); Message("Could not save settings. Automation paused: " + ex.Message); return false; }
    }

    private async void AutomationClick(object sender, RoutedEventArgs e)
        => await ToggleAutomationAsync();

    private async Task ToggleAutomationAsync()
    {
        if (settings.AutoEnabled)
        {
            settings.AutoEnabled = false;
            await ApplySelectedProfileAsync(selectedNightProfile);
            return;
        }
        else
        {
            if (!settings.HasLocation)
            {
                UpdateScheduleDisplay();
                if (!IsVisible) ShowFromTray();
                ShowLocation(enableAuto: true);
                return;
            }
            if (!monitors.Any(m => m.Enabled && m.CanControl))
            {
                Message("Enable at least one display with working DDC/CI first.");
                UpdateScheduleDisplay();
                return;
            }
            settings.AutoEnabled = true;
            Message("Automation enabled. Sunset Monitor follows your local sunrise and sunset.");
        }
        CancelQueuedPreview();
        SelectScheduledEditor();
        SaveSettings();
        await CycleAsync();
    }

    private async Task ResumeAsync()
    {
        if (!settings.HasLocation) { ShowFromTray(); ShowLocation(enableAuto: true); return; }
        settings.AutoEnabled = true; CancelQueuedPreview();
        SelectScheduledEditor();
        SaveSettings(); await CycleAsync();
    }

    private async Task ManualAsync(double amount)
    {
        if (!monitors.Any(m => m.Enabled && m.CanControl)) { Message("No enabled display can accept DDC/CI settings yet."); return; }
        CancelQueuedPreview();
        SelectProfileEditor(amount < 0.5);
        previewWholeProfile = true;
        manualDayAmount = amount; manualUntil = DateTimeOffset.UtcNow.AddHours(1); pauseUntil = null;
        if (!SaveSettings()) return;
        Message("Temporary profile applied for one hour. Resume schedule to end the override early.");
        await CycleAsync(true);
    }
    private async void DayClick(object sender, RoutedEventArgs e) => await ManualAsync(1);
    private async void NightClick(object sender, RoutedEventArgs e) => await ManualAsync(0);

    private async void DetectClick(object sender, RoutedEventArgs e)
    {
        await DetectLocationAsync();
    }

    private async Task DetectLocationAsync()
    {
        if (locationBusy) return;
        locationBusy = true;
        DetectButton.IsEnabled = false;
        DetectButton.Content = "Locating…";
        SkipLocationButton.IsEnabled = false;
        CitySearchExpander.IsEnabled = false;
        LocationHelpText.Visibility = Visibility.Visible;
        LocationHelpText.Text = "Allow access in the Windows prompt.";
        LocationPermissionButton.Visibility = Visibility.Collapsed;
        try
        {
            var detected = await detectLocation();
            if (exiting) return;
            await SaveDetectedLocationAsync(detected);
        }
        catch (Exception ex)
        {
            if (!diagnostic) App.Log(ex);
            string message = ex is LocationPermissionException or UnauthorizedAccessException
                ? "Location is off. Allow it in Windows, or choose a city below."
                : "Location wasn't found. Try again, or choose your city.";
            LocationHelpText.Text = message;
            LocationPermissionButton.Visibility = Visibility.Visible;
            Message(message);
        }
        finally
        {
            locationBusy = false;
            DetectButton.IsEnabled = true; DetectButton.Content = "Use my location";
            SkipLocationButton.IsEnabled = true; CitySearchExpander.IsEnabled = true;
        }
    }

    private async Task SaveDetectedLocationAsync(DetectedLocation detected)
    {
        SolarCalculator.ValidateCoordinates(detected.Latitude, detected.Longitude);
        TimeZoneInfo.FindSystemTimeZoneById(detected.TimeZoneId);
        settings.Latitude = detected.Latitude;
        settings.Longitude = detected.Longitude;
        settings.TimeZoneId = detected.TimeZoneId;
        settings.LocationName = detected.Name;
        settings.LocationPromptDismissed = true;
        if (enableAfterLocation) { settings.AutoEnabled = true; CancelQueuedPreview(); SelectScheduledEditor(); }
        else UpdateEditDeadline();
        bool saved = SaveSettings();
        PopulateLocation();
        if (saved)
        {
            LocationHelpText.Visibility = Visibility.Collapsed;
            CloseLocation();
            Message("Location saved · " + settings.LocationName);
        }
        else
        {
            LocationHelpText.Visibility = Visibility.Visible;
            LocationHelpText.Text = "Couldn't save your location. Please try again.";
        }
        await CycleAsync();
    }

    private void LocationPermissionsClick(object sender, RoutedEventArgs e)
    {
        if (diagnostic) { Message("Location settings link is disabled in demo mode."); return; }
        try { Process.Start(new ProcessStartInfo("ms-settings:privacy-location") { UseShellExecute = true }); }
        catch (Exception ex) { ReportError(ex); }
    }

    private async void SaveLocationClick(object sender, RoutedEventArgs e)
    {
        if (!TryCoordinate(LatitudeBox.Text, out double lat) || !TryCoordinate(LongitudeBox.Text, out double lon)
            || lat < -90 || lat > 90 || lon < -180 || lon > 180 || ZoneBox.SelectedItem is not TimeZoneInfo zone)
        { Message("Enter valid latitude (-90 to 90), longitude (-180 to 180) and a time zone."); return; }
        settings.Latitude = lat; settings.Longitude = lon; settings.TimeZoneId = zone.Id;
        settings.LocationName = CityCatalog.Label(lat, lon);
        settings.LocationPromptDismissed = true;
        UpdateEditDeadline();
        if (SaveSettings()) Message("Location saved · " + settings.LocationName);
        PopulateLocation(); await CycleAsync();
    }

    private static bool TryCoordinate(string text, out double value) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);

    private void FadeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!ready) return;
        settings.TransitionMinutes = (int)FadeSlider.Value;
        UpdateEditDeadline();
        FadeLabel.Text = settings.TransitionMinutes == 0 ? "Instant" : settings.TransitionMinutes + " min";
        ScheduleSave(); UpdateScheduleDisplay();
    }
    private void OffsetChanged(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        if (!int.TryParse(RiseOffsetBox.Text, out int rise) || !int.TryParse(SetOffsetBox.Text, out int set)
            || rise < -120 || rise > 120 || set < -120 || set > 120)
        {
            Message("Offsets must be whole minutes between -120 and 120.");
            RiseOffsetBox.Text = settings.SunriseOffsetMinutes.ToString(); SetOffsetBox.Text = settings.SunsetOffsetMinutes.ToString(); return;
        }
        settings.SunriseOffsetMinutes = rise; settings.SunsetOffsetMinutes = set;
        UpdateEditDeadline(); ScheduleSave(); UpdateScheduleDisplay();
    }

    private async void RefreshClick(object sender, RoutedEventArgs e) { refreshRequested = true; await CycleAsync(); }
    private async void RestoreClick(object sender, RoutedEventArgs e) => await RestoreAsync();
    private async Task RestoreAsync()
    {
        applyProfileOnStartup = false;
        settings.AutoEnabled = false; CancelQueuedPreview();
        SaveSettings(); UpdateScheduleDisplay();
        var results = diagnostic ? new List<MonitorWriteResult>() : await hardware.RestoreAsync(settings);
        Message(results.All(r => r.Success) ? "Original monitor settings restored. Automation is paused." : "Some original settings could not be restored; refresh displays and retry.");
        refreshRequested = true; await CycleAsync();
    }

    private void StartupClick(object sender, RoutedEventArgs e)
    {
        if (diagnostic) return;
        try
        {
            StartupRegistration.SetEnabled(StartupCheck.IsChecked == true);
            Message(StartupCheck.IsChecked == true ? $"{App.DisplayName} will start in the tray when you sign in." : "Start at sign-in disabled.");
        }
        catch (Exception ex) { StartupCheck.IsChecked = IsStartupEnabled(); ReportError(ex); }
    }
    private static bool IsStartupEnabled() => StartupRegistration.IsEnabled();

    private void DisplaysChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        // Resolution changes can arrive in bursts when entering or leaving a game.
        refreshRequested = true;
        UpdateCycleInterval();
    });
    private void PowerChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Dispatcher.BeginInvoke(async () => { refreshRequested = true; await CycleAsync(); });
    }
    private void TimeChanged(object? sender, EventArgs e) => Dispatcher.BeginInvoke(async () => { TimeZoneInfo.ClearCachedData(); await CycleAsync(); });

    private void CreateTray()
    {
        trayIcon = TrayArtwork.Create();
        Icon = Imaging.CreateBitmapSourceFromHIcon(trayIcon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Quick display controls", null, (_, _) => ShowTrayControls());
        menu.Items.Add("Open " + App.DisplayName, null, (_, _) => ShowFromTray());
        menu.Items.Add("Resume automatic schedule", null, async (_, _) => await ResumeAsync());
        menu.Items.Add("Pause for 1 hour", null, (_, _) =>
        {
            CancelQueuedPreview();
            if (settings.AutoEnabled) pauseUntil = DateTimeOffset.UtcNow.AddHours(1);
            UpdateScheduleDisplay();
        });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Day profile for 1 hour", null, async (_, _) => await ManualAsync(1));
        menu.Items.Add("Night profile for 1 hour", null, async (_, _) => await ManualAsync(0));
        menu.Items.Add("Restore original settings and pause", null, async (_, _) => await RestoreAsync());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Quit " + App.DisplayName, null, async (_, _) => await ExitAsync());
        tray = new Forms.NotifyIcon { Icon = trayIcon, Text = App.DisplayName, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowTrayControls(); };
        tray.DoubleClick += (_, _) => ShowFromTray();
    }

    public void ShowFromTray() { trayControls?.Hide(); Show(); WindowState = WindowState.Normal; Activate(); PromptLocationOnFirstLaunch(false); }
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private async void QuitClick(object sender, RoutedEventArgs e) => await ExitAsync();
    private void OnClosing(object? sender, CancelEventArgs e) { if (!exiting) { e.Cancel = true; Hide(); } }

    public async Task ExitAsync()
    {
        if (exiting) return;
        exiting = true; timer.Stop(); saveTimer.Stop(); SaveSettings();
        weatherCancellation?.Cancel();
        if (!diagnostic)
        {
            SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            SystemEvents.PowerModeChanged -= PowerChanged;
            SystemEvents.TimeChanged -= TimeChanged;
        }
        tray?.Dispose(); tray = null; trayIcon?.Dispose();
        trayControls?.CloseForExit(); trayControls = null;
        ReleasePresetHotkeys();
        await hardware.DisposeAsync();
        Close(); Application.Current.Shutdown();
    }
    private void Message(string text) { MessageText.Text = text; MessageText.ToolTip = text; if (saveFailed) UpdateTrayControls(); }
    public void ReportError(Exception ex) { App.Log(ex); Message("Could not complete that action: " + ex.Message); }

    public async Task RenderAsync(string output)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        var bitmap = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(this);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(output); encoder.Save(file);
    }

    public async Task RunUiSmokeAsync(string output)
    {
        if (!diagnostic) throw new InvalidOperationException("UI smoke tests require demo mode.");
        var report = new List<string>();
        void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); report.Add("PASS " + text); }
        CheckStartupRegistration(Check);
        var executableInfo = FileVersionInfo.GetVersionInfo(Environment.ProcessPath!);
        Check(Title == App.DisplayName && App.VersionLabel.StartsWith(App.DisplayName + " ", StringComparison.Ordinal)
            && executableInfo.ProductName == App.DisplayName && executableInfo.FileDescription == App.DisplayName,
            "Main window, version label, and executable metadata use Sunset Monitor branding");
        Check(monitors.Count == 3 && !settings.AutoEnabled && monitors[1].IsPrimary
            && monitors.Select(m => m.DisplayNumber).SequenceEqual(["2", "1", "3"]), "Starts paused with monitors in desk order and primary display centered");
        AutomationClick(this, new RoutedEventArgs());
        await CycleAsync();
        Check(settings.AutoEnabled, "Enable automation");
        FadeSlider.Value = 45;
        Check(settings.TransitionMinutes == 45, "Fade duration binding");
        monitors[0].NightBrightness = 17;
        Check(settings.Monitors[monitors[0].Id].NightBrightness == 17, "Per-monitor profile binding");
        await ManualAsync(0);
        Check(manualDayAmount == 0 && manualUntil.HasValue, "Temporary night override");
        await ResumeAsync();
        Check(manualDayAmount is null && settings.AutoEnabled, "Resume clears override");
        await RestoreAsync();
        Check(!settings.AutoEnabled && manualDayAmount is null, "Restore pauses automation");
        LatitudeBox.Text = "not a coordinate";
        double? old = settings.Latitude;
        SaveLocationClick(this, new RoutedEventArgs());
        Check(settings.Latitude == old, "Invalid coordinate rejected without changing saved location");
        Check(!ManualLocationExpander.IsExpanded, "Manual coordinate entry is optional and collapsed");
        detectLocation = () => Task.FromResult(new DetectedLocation("Warsaw", 52.2297, 21.0122, "Central European Standard Time"));
        await DetectLocationAsync();
        Check(settings.LocationName == "Warsaw" && settings.Latitude == 52.2297 && !settings.AutoEnabled,
            "Allowed location is saved automatically without enabling monitor automation");
        detectLocation = () => Task.FromException<DetectedLocation>(new LocationPermissionException("Denied for test"));
        await DetectLocationAsync();
        Check(settings.Latitude == 52.2297 && settings.LocationName == "Warsaw" && DetectButton.IsEnabled,
            "Denied permission preserves the saved location and enables retry");
        detectLocation = () => Task.FromException<DetectedLocation>(new TimeoutException("No location for test"));
        await DetectLocationAsync();
        Check(settings.Latitude == 52.2297 && DetectButton.IsEnabled, "Location timeout preserves settings and enables retry");
        detectLocation = () => Task.FromResult(new DetectedLocation("Warsaw", 52.2297, 21.0122, "Central European Standard Time"));
        await DetectLocationAsync();
        PopulateLocation();
        FadeSlider.Value = 30;
        DayTab.IsChecked = true;
        NightTab.IsChecked = true;
        Check(monitors.All(m => m.IsNightProfile && m.CurrentBrightness == m.NightBrightness) && !settings.AutoEnabled,
            "Night selection applies saved values immediately while Auto remains off");
        await RenderThemeTransitionAsync(Path.GetDirectoryName(output)!, "night");
        await RenderAsync(Path.Combine(Path.GetDirectoryName(output)!, "theme-night.png"));
        await Task.Delay(1800);
        await RenderAsync(Path.Combine(Path.GetDirectoryName(output)!, "theme-night-drift.png"));
        monitors[0].SelectedBrightness = 25;
        await FlushSliderEditsAsync();
        Check(monitors[0].NightBrightness == 25 && monitors[0].CurrentBrightness == 25,
            "Selected night brightness slider applies current setting immediately");
        monitors[0].SelectedSaturation = 72;
        Check(monitors[0].NightSaturation == 72 && monitors[0].DaySaturation == 50
            && pendingSliderEdits[monitors[0].Id].SetEquals([nameof(MonitorViewModel.NightSaturation)]),
            "Saturation slider edits only the selected profile's saturation");
        await FlushSliderEditsAsync();
        monitors[0].SelectedContrast = 42;
        Check(monitors[0].NightContrast == 42 && monitors[0].DayContrast == 40,
            "Single contrast control edits the selected profile");
        await FlushSliderEditsAsync();
        DayTab.IsChecked = true;
        Check(monitors[0].SelectedSaturation == 50, "Day/night selector updates saturation value");
        await RenderThemeTransitionAsync(Path.GetDirectoryName(output)!, "day");
        monitors[0].SelectedSaturation = 60;
        await FlushSliderEditsAsync();
        Check(monitors[0].DaySaturation == 60 && monitors[0].NightSaturation == 72,
            "Saturation saves separate day and night settings");
        var modernProfile = new MonitorProfile { SaturationSource = "ddc", OriginalSaturationRaw = 50 };
        Check(!MigrateLegacyColorPreset(modernProfile, DemoReadings()[1]) && !modernProfile.ColorPresetRestorePending,
            "Recorded saturation is never restored as a color preset");
        var legacyProfile = new MonitorProfile { OriginalSaturationRaw = 11, DaySaturation = 100, NightSaturation = 0 };
        bool migratedPreset = MigrateLegacyColorPreset(legacyProfile, DemoReadings()[1]);
        Check(migratedPreset && legacyProfile.OriginalColorPresetRaw == 11 && legacyProfile.ColorPresetRestorePending
              && legacyProfile.OriginalSaturationRaw == 50 && legacyProfile.DaySaturation == 50 && legacyProfile.NightSaturation == 50,
            "Legacy color preset is restored and real saturation starts from the monitor value");
        UpdateLayout();
        var sliders = VisualChildren<Slider>(MonitorItems).Where(s => s.Orientation == Orientation.Vertical).ToList();
        Check(sliders.Count == 9, "Three visible controls per display");
        var brightnessSlider = sliders.First(s => s.DataContext == monitors[0] && System.Windows.Automation.AutomationProperties.GetName(s) == "Brightness");
        brightnessSlider.SetCurrentValue(Slider.ValueProperty, 0d);
        UpdateLayout();
        var track = (System.Windows.Controls.Primitives.Track)brightnessSlider.Template.FindName("PART_Track", brightnessSlider);
        double bottom = track.Thumb.TranslatePoint(new Point(), brightnessSlider).Y;
        brightnessSlider.SetCurrentValue(Slider.ValueProperty, 100d);
        UpdateLayout();
        double top = track.Thumb.TranslatePoint(new Point(), brightnessSlider).Y;
        Check(top < bottom && monitors[0].DayBrightness == 100, "Vertical brightness runs from low at bottom to high at top and updates its binding");
        Slider.DecreaseSmall.Execute(null, brightnessSlider);
        Check(monitors[0].DayBrightness == 99, "Brightness supports one-step keyboard commands");
        brightnessSlider.SetCurrentValue(Slider.ValueProperty, 55d);
        await FlushSliderEditsAsync();

        settings.Latitude = null; settings.Longitude = null; settings.LocationPromptDismissed = false;
        startupPromptConsidered = false;
        PromptLocationOnFirstLaunch(true);
        Check(LocationLayer.Visibility != Visibility.Visible, "Quiet tray startup does not interrupt with location setup");
        PromptLocationOnFirstLaunch(false);
        Check(LocationLayer.Visibility == Visibility.Visible && !MainSurface.IsEnabled, "First visible launch prompts for location");
        string directory = Path.GetDirectoryName(output)!;
        await RenderAsync(Path.Combine(directory, "redesign-onboarding.png"));
        DismissLocation();
        startupPromptConsidered = false;
        PromptLocationOnFirstLaunch(false);
        Check(settings.LocationPromptDismissed && LocationLayer.Visibility != Visibility.Visible && MainSurface.IsEnabled,
            "Not now dismisses setup and prevents repeated startup prompts");
        ShowLocation(enableAuto: true);
        detectLocation = () => Task.FromException<DetectedLocation>(new LocationPermissionException("Denied for test"));
        await DetectLocationAsync();
        Check(!settings.HasLocation && !settings.AutoEnabled && LocationLayer.Visibility == Visibility.Visible
            && LocationPermissionButton.Visibility == Visibility.Visible, "Denied setup keeps manual sliders available and provides a retry path");
        CitySearchBox.Text = "Warsaw";
        Check(CityResults.Items.Count > 0, "City search works without location permission");
        var city = CityCatalog.Search("Warsaw").First(c => c.Country == "PL");
        await SaveDetectedLocationAsync(new(city.Name, city.Latitude, city.Longitude, city.WindowsTimeZoneId));
        Check(settings.HasLocation && settings.AutoEnabled && LocationLayer.Visibility != Visibility.Visible,
            "Choosing a city completes a pending auto request");
        await RestoreAsync();
        SettingsClick(this, new RoutedEventArgs());
        Check(SettingsLayer.Visibility == Visibility.Visible && !MainSurface.IsEnabled, "Settings open separately from monitor controls");
        await RenderAsync(Path.Combine(directory, "redesign-settings.png"));
        CloseSettings();
        Check(MainSurface.IsEnabled && SettingsLayer.Visibility != Visibility.Visible, "Closing settings returns to monitor controls");
        double originalWidth = Width, originalHeight = Height;
        Width = MinWidth; Height = MinHeight;
        await RenderAsync(Path.Combine(directory, "redesign-small.png"));
        ShowLocation();
        CitySearchExpander.IsExpanded = true;
        CitySearchBox.Text = "London";
        CityResults.SelectedIndex = 0;
        string savedCity = settings.LocationName;
        await RenderAsync(Path.Combine(directory, "redesign-city-search.png"));
        Check(LocationCard.ActualHeight <= ActualHeight - 47 && CitySearchBox.IsVisible,
            "Expanded location setup fits a small window with scrollable content");
        Check(settings.LocationName == savedCity && LocationLayer.Visibility == Visibility.Visible,
            "Keyboard city selection waits for explicit confirmation");
        CloseLocation();
        CitySearchExpander.IsExpanded = false;
        Width = originalWidth; Height = originalHeight;
        await CheckLinkedControlsAsync(Check, directory);
        await CheckSaturationAppearanceAsync(Check, directory);
        await CheckNewDesignAsync(Check, directory);
        await CheckSunFeaturesAsync(Check, directory);
        await CheckAdditionalSettingsAsync(Check, directory);
        await CheckReviewRegressionsAsync(Check);
        await CheckProfileModesAsync(Check, directory);
        await CheckTrayControlsAsync(Check, directory);
        await CheckBackgroundActivityAsync(Check);
        await CheckTrayAvailabilityAsync(Check);
        await CheckTrayAutoAsync(Check);
        Message("Changes apply instantly and save automatically.");
        File.WriteAllLines(output, report);
    }

    private static IEnumerable<T> VisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) yield return result;
            foreach (var descendant in VisualChildren<T>(child)) yield return descendant;
        }
    }
}
