using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;

namespace SolarDesk;

public partial class MainWindow
{
    private HwndSource? hotkeySource;
    private readonly HashSet<int> registeredPresetKeys = [];

    private void PresetSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox { DataContext: MonitorViewModel monitor, SelectedItem: MonitorPreset preset }) return;
        monitor.LoadPresetEditor(preset);
    }
    private void SavePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MonitorViewModel monitor }) return;
        SaveMonitorPreset(monitor);
    }
    private bool SaveMonitorPreset(MonitorViewModel monitor)
    {
        string name = monitor.PresetName.Trim();
        if (name.Length == 0) { Message("Give the preset a name, such as Reading or Movies."); return false; }
        name = name[..Math.Min(40, name.Length)];
        var existing = monitor.Profile.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is null && monitor.Profile.Presets.Count >= 30) { Message("This display already has 30 presets. Delete one first."); return false; }
        int key = Math.Clamp(monitor.PresetHotkey, 0, 9);
        if (key != 0 && settings.Monitors.Values.SelectMany(p => p.Presets).Any(p => !ReferenceEquals(p, existing) && p.Hotkey == key))
        { Message("That shortcut belongs to another preset. Choose a different shortcut."); return false; }
        var preset = existing ?? new MonitorPreset();
        preset.Name = name; preset.Hotkey = key;
        preset.Values = new(monitor.SelectedBrightness, monitor.SelectedContrast, monitor.SelectedSaturation);
        var advanced = monitor.SelectedAdvanced;
        foreach (var control in monitor.AdvancedControls)
            if (advanced.Get(control.Control.Kind) is null && control.Control.Kind is AdvancedKind.Sharpness or AdvancedKind.Gamma)
                advanced = advanced.Set(control.Control.Kind, (uint)control.Value);
        preset.Advanced = advanced;
        if (existing is null) monitor.Profile.Presets.Add(preset);
        monitor.SelectedPreset = preset; monitor.RefreshPresets();
        if (!SaveSettings()) return false;
        Message("Saved " + name + " for " + monitor.ShortName + ".");
        RegisterPresetHotkeys();
        return true;
    }
    private async void ApplyPresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: MonitorViewModel monitor } && monitor.SelectedPreset is MonitorPreset preset)
            await ApplyMonitorPresetAsync(monitor, preset);
        else Message("Choose a saved preset first.");
    }
    private async Task ApplyMonitorPresetAsync(MonitorViewModel monitor, MonitorPreset preset)
    {
        if (monitor.HasBrightness) monitor.SelectedBrightness = preset.Values.Brightness;
        if (monitor.HasContrast) monitor.SelectedContrast = preset.Values.Contrast;
        if (monitor.HasSaturation) monitor.SelectedSaturation = preset.Values.Saturation;
        bool hasAdvanced = false;
        foreach (var control in monitor.AdvancedControls)
            if (preset.Advanced.Get(control.Control.Kind) is uint value && control.Control.Accepts(value))
            { monitor.SetAdvanced(control.Control, value); hasAdvanced = true; }
        if (hasAdvanced)
        {
            monitor.Profile.AdvancedManaged = true;
            ProfileChanged(monitor, SelectedProperty("Advanced"));
        }
        foreach (string control in ControlNames.Where(name => SupportsControl(monitor, SelectedProperty(name))))
            ProfileChanged(monitor, SelectedProperty(control));
        await FlushSliderEditsAsync(force: true);
    }
    private void DeletePresetClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: MonitorViewModel monitor } || monitor.SelectedPreset is not MonitorPreset preset) return;
        monitor.Profile.Presets.Remove(preset); monitor.SelectedPreset = null; monitor.PresetName = ""; monitor.PresetHotkey = 0; monitor.RefreshPresets();
        SaveSettings(); RegisterPresetHotkeys(); Message("Preset deleted.");
    }
    private void InitializePresetHotkeys()
    {
        if (diagnostic) return;
        // Tray startup needs a message handle for shortcuts without rendering the window.
        hotkeySource = HwndSource.FromHwnd(new WindowInteropHelper(this).EnsureHandle());
        hotkeySource?.AddHook(PresetWindowMessage);
        RegisterPresetHotkeys();
    }
    private void RegisterPresetHotkeys()
    {
        if (diagnostic || hotkeySource is null) return;
        foreach (int key in registeredPresetKeys) HotkeyNative.UnregisterHotKey(hotkeySource.Handle, 0x5000 + key);
        registeredPresetKeys.Clear();
        foreach (int key in settings.Monitors.Values.SelectMany(m => m.Presets).Select(p => p.Hotkey).Where(k => k is >= 1 and <= 9).Distinct())
        {
            // Alt + Ctrl + Shift, with Windows' no-repeat flag.
            if (HotkeyNative.RegisterHotKey(hotkeySource.Handle, 0x5000 + key, 0x4007, (uint)(0x30 + key))) registeredPresetKeys.Add(key);
            else Message("Ctrl + Alt + Shift + " + key + " is already in use. The preset still works from its Apply button.");
        }
    }
    private IntPtr PresetWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != 0x0312) return IntPtr.Zero;
        long id = wParam.ToInt64();
        if (id < 0x5001 || id > 0x5009) return IntPtr.Zero;
        int key = (int)(id - 0x5000);
        if (registeredPresetKeys.Contains(key))
        { handled = true; _ = ApplyPresetHotkeyAsync(key); }
        return IntPtr.Zero;
    }
    private async Task ApplyPresetHotkeyAsync(int key)
    {
        try
        {
            foreach (var (id, profile) in settings.Monitors)
                if (profile.Presets.FirstOrDefault(p => p.Hotkey == key) is MonitorPreset preset)
                {
                    var monitor = monitors.FirstOrDefault(m => m.Id == id);
                    if (monitor is null) Message("Connect the display for " + preset.Name + " first.");
                    else await ApplyMonitorPresetAsync(monitor, preset);
                    return;
                }
        }
        catch (Exception ex) { ReportError(ex); }
    }
    private void ReleasePresetHotkeys()
    {
        if (hotkeySource is null) return;
        foreach (int key in registeredPresetKeys) HotkeyNative.UnregisterHotKey(hotkeySource.Handle, 0x5000 + key);
        registeredPresetKeys.Clear(); hotkeySource.RemoveHook(PresetWindowMessage); hotkeySource = null;
    }
    private static class HotkeyNative
    {
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    }
}
