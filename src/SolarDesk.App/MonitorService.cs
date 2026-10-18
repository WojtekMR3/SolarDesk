using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace SolarDesk;

public sealed record MonitorReading(string Id, string Name, string Display, uint? Brightness, uint BrightnessMax,
    uint? Contrast, uint ContrastMax, uint? Saturation, uint SaturationMax, bool IsPrimary, string Status,
    int Left = 0, int Top = 0, string? SaturationSource = null, int? NvidiaVibranceRaw = null,
    int PixelWidth = 0, int PixelHeight = 0, List<AdvancedControl>? Advanced = null)
{
    public string? AdvancedCapabilities { get; init; }
    public uint? ColorPresetRaw { get; init; }
    public int BrightnessPercent => Brightness.HasValue && BrightnessMax > 0 ? (int)Math.Round(Brightness.Value * 100.0 / BrightnessMax) : 0;
    public int ContrastPercent => Contrast.HasValue && ContrastMax > 0 ? (int)Math.Round(Contrast.Value * 100.0 / ContrastMax) : 0;
    public int SaturationPercent => Saturation.HasValue && SaturationMax > 0 ? (int)Math.Round(Saturation.Value * 100.0 / SaturationMax) : 0;
}
public sealed record MonitorTarget(string Id, int? Brightness, int? Contrast, int? Saturation = null, AdvancedValues? Advanced = null);
public sealed record MonitorWriteResult(string Id, bool Success, string Message, AdvancedValues? Advanced = null);

/// <summary>Owns physical handles. All DXVA2 operations run serially on a worker thread.</summary>
public sealed partial class MonitorService : IAsyncDisposable
{
    private const byte ColorPresetCode = 0x14;
    private const byte SaturationCode = 0x8A;
    private sealed record Device(IntPtr Handle, MonitorReading Reading);
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly List<Device> devices = [];
    private readonly NvidiaVibrance vibrance = new();
    private readonly Dictionary<(string, byte), uint> lastValues = [];
    private readonly Dictionary<(string, byte), DateTimeOffset> retryAfter = [];

    public async Task<List<MonitorReading>> ScanAsync()
    {
        await gate.WaitAsync();
        try { return await Task.Run(Scan); }
        finally { gate.Release(); }
    }

    private List<MonitorReading> Scan()
    {
        ReleaseHandles();
        var readings = new List<MonitorReading>();
        Native.MonitorEnum callback = (IntPtr hMonitor, IntPtr hdc, ref Native.Rect rect, IntPtr data) =>
        {
            var info = new Native.MonitorInfo { Size = Marshal.SizeOf<Native.MonitorInfo>() };
            Native.GetMonitorInfo(hMonitor, ref info);
            string display = info.Device ?? "Display";
            var mode = new Native.DisplayMode { Size = (ushort)Marshal.SizeOf<Native.DisplayMode>() };
            bool hasMode = Native.EnumDisplaySettings(display, -1, ref mode);
            var displayDevice = new Native.DisplayDevice { Size = Marshal.SizeOf<Native.DisplayDevice>() };
            Native.EnumDisplayDevices(display, 0, ref displayDevice, 1);
            string stableId = string.IsNullOrWhiteSpace(displayDevice.DeviceId) ? display : displayDevice.DeviceId;
            var identity = GetEdidIdentity(stableId);
            if (identity.StableId is not null) stableId = identity.StableId;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) || count == 0) return true;
            var physical = new Native.PhysicalMonitor[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) return true;
            for (int i = 0; i < physical.Length; i++)
            {
                var monitor = physical[i];
                string id = stableId + (physical.Length > 1 ? "|" + i : "");
                uint? brightness = Read(monitor.Handle, 0x10, out uint brightnessMax);
                uint? contrast = Read(monitor.Handle, 0x12, out uint contrastMax);
                uint? saturation = Read(monitor.Handle, SaturationCode, out uint saturationMax);
                string? saturationSource = saturation.HasValue ? "ddc" : null;
                int? nvidiaRaw = null;
                if (!saturation.HasValue && vibrance.TryRead(display, out var color))
                {
                    saturation = (uint)color.Percent;
                    saturationMax = 100;
                    saturationSource = "nvidia";
                    nvidiaRaw = color.Current;
                }
                string name = identity.Name ?? (string.IsNullOrWhiteSpace(monitor.Description) ? displayDevice.DeviceString : monitor.Description);
                name = (name ?? "Monitor") + " · " + display.Replace(@"\\.\DISPLAY", "Display ");
                var statusParts = new List<string>();
                if (brightness.HasValue) statusParts.Add("Brightness");
                if (contrast.HasValue) statusParts.Add("Contrast");
                if (saturation.HasValue) statusParts.Add("Saturation");
                string status = statusParts.Count == 0 ? "No DDC/CI response · enable it in the monitor menu"
                    : $"Ready · {statusParts.Count} controls";
                var additional = ReadAdvanced(monitor.Handle, out string additionalReport);
                uint? colorPreset = Native.GetVCPFeatureAndVCPFeatureReply(monitor.Handle, ColorPresetCode, IntPtr.Zero, out uint preset, out _) ? preset : null;
                // A last-requested temperature is not the current white point in a custom color mode.
                if (colorPreset is not (>= 3 and <= 10))
                    additional = additional.Select(c => c.Kind == AdvancedKind.Temperature && c.TemperatureStep > 0 ? c with { Current = 0 } : c).ToList();
                var reading = new MonitorReading(id, name ?? display, display, brightness, brightnessMax, contrast, contrastMax,
                    saturation, saturationMax, (info.Flags & 1) != 0, status, rect.Left, rect.Top, saturationSource, nvidiaRaw,
                    hasMode ? (int)mode.PixelWidth : 0, hasMode ? (int)mode.PixelHeight : 0, additional) { AdvancedCapabilities = additionalReport, ColorPresetRaw = colorPreset };
                devices.Add(new Device(monitor.Handle, reading));
                readings.Add(reading);
                if (brightness.HasValue) lastValues[(id, 0x10)] = brightness.Value;
                if (contrast.HasValue) lastValues[(id, 0x12)] = contrast.Value;
                if (saturation.HasValue) lastValues[(id, SaturationCode)] = saturation.Value;
            }
            return true;
        };
        if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        // Some vendors reuse EDID serial numbers. Keep those displays independently addressable.
        var duplicateIds = devices.GroupBy(d => d.Reading.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet();
        if (duplicateIds.Count > 0)
        {
            for (int i = 0; i < devices.Count; i++)
                if (duplicateIds.Contains(devices[i].Reading.Id))
                {
                    var updated = devices[i].Reading with { Id = devices[i].Reading.Id + "|" + devices[i].Reading.Display + "|" + i };
                    devices[i] = devices[i] with { Reading = updated };
                    readings[i] = updated;
                }
            lastValues.Clear();
            foreach (var reading in readings)
            {
                if (reading.Brightness is uint b) lastValues[(reading.Id, 0x10)] = b;
                if (reading.Contrast is uint c) lastValues[(reading.Id, 0x12)] = c;
                if (reading.Saturation is uint s) lastValues[(reading.Id, SaturationCode)] = s;
            }
        }
        return readings;
    }

    private static (string? Name, string? StableId) GetEdidIdentity(string deviceId)
    {
        try
        {
            string[] parts = deviceId.Split('#');
            if (parts.Length < 3) return (null, null);
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\DISPLAY\" + parts[1] + @"\" + parts[2] + @"\Device Parameters");
            if (key?.GetValue("EDID") is not byte[] edid || edid.Length < 128) return (parts[1], null);
            string? name = null, serial = null;
            for (int offset = 54; offset + 18 <= 126; offset += 18)
            {
                if (edid[offset] != 0 || edid[offset + 1] != 0 || edid[offset + 2] != 0) continue;
                string value = Encoding.ASCII.GetString(edid, offset + 5, 13).Trim('\0', '\n', '\r', ' ');
                if (edid[offset + 3] == 0xFC) name = value;
                if (edid[offset + 3] == 0xFF) serial = value;
            }
            uint numericSerial = BitConverter.ToUInt32(edid, 12);
            if (string.IsNullOrWhiteSpace(serial) && numericSerial != 0 && numericSerial != uint.MaxValue) serial = numericSerial.ToString();
            // A real hardware serial survives port changes. Otherwise retain the unique Windows instance path.
            bool usefulSerial = !string.IsNullOrWhiteSpace(serial) && serial.Any(c => c != '0' && c != ' ');
            return (string.IsNullOrWhiteSpace(name) ? parts[1] : name, usefulSerial ? "EDID:" + parts[1] + ":" + serial : null);
        }
        catch { return (null, null); }
    }

    private static uint? Read(IntPtr handle, byte code, out uint max)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (Native.GetVCPFeatureAndVCPFeatureReply(handle, code, IntPtr.Zero, out uint current, out max)
                && max > 0 && current <= max) return current;
            Thread.Sleep(60);
        }
        max = 0;
        return null;
    }

    public async Task<List<MonitorWriteResult>> ApplyAsync(IReadOnlyList<MonitorTarget> targets, bool force = false)
    {
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() => targets.Select(target =>
            {
                var device = devices.FirstOrDefault(d => d.Reading.Id == target.Id);
                if (device is null) return new MonitorWriteResult(target.Id, false, "Monitor disconnected; refresh displays.");
                var errors = new List<string>();
                if (target.Brightness.HasValue && device.Reading.Brightness.HasValue)
                    Set(device, 0x10, PercentToRaw(target.Brightness.Value, device.Reading.BrightnessMax), force, errors);
                if (target.Contrast.HasValue && device.Reading.Contrast.HasValue)
                    Set(device, 0x12, PercentToRaw(target.Contrast.Value, device.Reading.ContrastMax), force, errors);
                if (target.Saturation.HasValue && device.Reading.Saturation.HasValue)
                {
                    if (device.Reading.SaturationSource == "nvidia") SetVibrance(device, target.Saturation.Value, force, errors);
                    else Set(device, SaturationCode, PercentToRaw(target.Saturation.Value, device.Reading.SaturationMax), force, errors);
                }
                else if (target.Saturation.HasValue) errors.Add("Saturation is unavailable on this display.");
                if (target.Advanced is not null) ApplyAdvanced(device, target.Advanced, force, errors);
                return new MonitorWriteResult(target.Id, errors.Count == 0, errors.Count == 0 ? "Applied" : string.Join(" · ", errors), LatestAdvanced(device));
            }).ToList());
        }
        finally { gate.Release(); }
    }

    public static uint PercentToRaw(int percent, uint maximum) => (uint)Math.Round(Math.Clamp(percent, 0, 100) * maximum / 100.0, MidpointRounding.AwayFromZero);

    private void SetVibrance(Device device, int percent, bool force, List<string> errors)
    {
        var key = (device.Reading.Id, SaturationCode);
        uint value = (uint)Math.Clamp(percent, 0, 100);
        if (!force && lastValues.TryGetValue(key, out uint previous) && previous == value) return;
        if (!force && retryAfter.TryGetValue(key, out var deadline) && deadline > DateTimeOffset.UtcNow)
        { errors.Add("Saturation unavailable; retrying shortly"); return; }
        string? error = vibrance.Set(device.Reading.Display, (int)value);
        if (error is null) { lastValues[key] = value; retryAfter.Remove(key); }
        else { errors.Add(error); retryAfter[key] = DateTimeOffset.UtcNow.AddSeconds(60); }
    }

    private void Set(Device device, byte code, uint raw, bool force, List<string> errors)
    {
        var key = (device.Reading.Id, code);
        if (!force && lastValues.TryGetValue(key, out uint previous) && previous == raw) return;
        if (!force && retryAfter.TryGetValue(key, out var deadline) && deadline > DateTimeOffset.UtcNow)
        { errors.Add("Monitor not responding; retrying shortly"); return; }
        int error = 0;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (Native.SetVCPFeature(device.Handle, code, raw))
            { lastValues[key] = raw; retryAfter.Remove(key); Thread.Sleep(45); return; }
            error = Marshal.GetLastWin32Error();
            Thread.Sleep(80);
        }
        retryAfter[key] = DateTimeOffset.UtcNow.AddSeconds(60);
        errors.Add((code == 0x10 ? "Brightness" : code == 0x12 ? "Contrast" : code == ColorPresetCode ? "Color preset" : "Saturation") + " write failed (" + error + ")");
    }

    public async Task<List<MonitorWriteResult>> RestoreAsync(AppSettings settings)
    {
        var originals = settings.Monitors.ToDictionary(pair => pair.Key,
                pair => (Brightness: pair.Value.OriginalBrightnessRaw, Contrast: pair.Value.OriginalContrastRaw,
                    Saturation: pair.Value.OriginalSaturationRaw, Vibrance: pair.Value.OriginalVibranceRaw,
                    ColorPreset: pair.Value.OriginalColorPresetRaw, Advanced: pair.Value.OriginalAdvanced, Managed: pair.Value.AdvancedManaged));
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() => devices.Select(device =>
            {
                var errors = new List<string>();
                if (originals.TryGetValue(device.Reading.Id, out var profile))
                {
                    if (profile.Brightness is uint brightness && device.Reading.Brightness.HasValue)
                        Set(device, 0x10, Math.Min(brightness, device.Reading.BrightnessMax), true, errors);
                    if (profile.Contrast is uint contrast && device.Reading.Contrast.HasValue)
                        Set(device, 0x12, Math.Min(contrast, device.Reading.ContrastMax), true, errors);
                    if (profile.Saturation is uint saturation && device.Reading.Saturation.HasValue && device.Reading.SaturationSource == "ddc")
                        Set(device, SaturationCode, Math.Min(saturation, device.Reading.SaturationMax), true, errors);
                    if (profile.Vibrance is int original && device.Reading.SaturationSource == "nvidia")
                    {
                        string? error = vibrance.Set(device.Reading.Display, original, raw: true);
                        if (error is not null) errors.Add(error);
                        lastValues.Remove((device.Reading.Id, SaturationCode));
                    }
                    if (profile.Managed)
                    {
                        // Restore exact RGB readings, then the original picture preset. A temperature
                        // request can be stale when the monitor originally used custom RGB.
                        var advanced = profile.Advanced.Red.HasValue || profile.Advanced.Green.HasValue || profile.Advanced.Blue.HasValue
                            ? profile.Advanced with { Temperature = null } : profile.Advanced;
                        ApplyAdvanced(device, advanced, true, errors);
                    }
                    if (profile.ColorPreset is uint colorPreset)
                        Set(device, ColorPresetCode, colorPreset, true, errors);
                }
                return new MonitorWriteResult(device.Reading.Id, errors.Count == 0, errors.Count == 0 ? "Original settings restored" : string.Join(" · ", errors));
            }).ToList());
        }
        finally { gate.Release(); }
    }

    public async Task<List<MonitorWriteResult>> RestoreLegacyColorPresetsAsync(AppSettings settings)
    {
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() => devices
                .Where(device => settings.Monitors.TryGetValue(device.Reading.Id, out var profile)
                    && profile.ColorPresetRestorePending && profile.OriginalColorPresetRaw.HasValue)
                .Select(device =>
                {
                    var profile = settings.Monitors[device.Reading.Id];
                    var errors = new List<string>();
                    Set(device, ColorPresetCode, profile.OriginalColorPresetRaw!.Value, true, errors);
                    if (errors.Count == 0) profile.ColorPresetRestorePending = false;
                    return new MonitorWriteResult(device.Reading.Id, errors.Count == 0,
                        errors.Count == 0 ? "Previous color preset restored" : string.Join(" · ", errors));
                }).ToList());
        }
        finally { gate.Release(); }
    }

    // Diagnostic writes the exact value just read, then reads it back. No profile is applied.
    public async Task<List<string>> VerifyCurrentValuesAsync()
    {
        await gate.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var results = new List<string>();
                foreach (var device in devices)
                    foreach (byte code in new byte[] { 0x10, 0x12, SaturationCode })
                    {
                        uint? before = Read(device.Handle, code, out _);
                        if (!before.HasValue) { results.Add($"{device.Reading.Name} 0x{code:X2}: unsupported"); continue; }
                        bool written = Native.SetVCPFeature(device.Handle, code, before.Value);
                        Thread.Sleep(100);
                        uint? after = Read(device.Handle, code, out _);
                        results.Add($"{device.Reading.Name} 0x{code:X2}: before={before}, after={after}, same-value write={(written && after == before ? "PASS" : "FAIL")}");
                    }
                return results;
            });
        }
        finally { gate.Release(); }
    }

    private void ReleaseHandles()
    {
        foreach (var device in devices) Native.DestroyPhysicalMonitor(device.Handle);
        devices.Clear(); lastValues.Clear(); retryAfter.Clear(); advancedCurrent.Clear(); advancedUnverified.Clear();
    }

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { await Task.Run(() => { ReleaseHandles(); vibrance.Dispose(); }); }
        finally { gate.Release(); }
    }

    private static partial class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct MonitorInfo
        {
            public int Size; public Rect Monitor; public Rect Work; public uint Flags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Device;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct DisplayDevice
        {
            public int Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct PhysicalMonitor
        {
            public IntPtr Handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Description;
        }
        internal delegate bool MonitorEnum(IntPtr monitor, IntPtr hdc, ref Rect rect, IntPtr data);
        // DEVMODEW: fixed native offsets keep the display/printer union out of managed layout.
        [StructLayout(LayoutKind.Explicit, Size = 220, CharSet = CharSet.Unicode)]
        internal struct DisplayMode
        {
            [FieldOffset(68)] public ushort Size;
            [FieldOffset(172)] public uint PixelWidth;
            [FieldOffset(176)] public uint PixelHeight;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplaySettings(string device, int mode, ref DisplayMode settings);
        [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnum callback, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool EnumDisplayDevices(string device, uint index, ref DisplayDevice info, uint flags);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr monitor, out uint count);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr monitor, uint count, [Out] PhysicalMonitor[] physical);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr monitor, byte code, IntPtr type, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetVCPFeature(IntPtr monitor, byte code, uint value);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool DestroyPhysicalMonitor(IntPtr monitor);
    }
}
