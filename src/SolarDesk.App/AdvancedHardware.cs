using System.Runtime.InteropServices;
using System.Text;

namespace SolarDesk;

public sealed partial class MonitorService
{
    private readonly Dictionary<(string, AdvancedKind), uint> advancedCurrent = [];
    private readonly HashSet<(string, AdvancedKind)> advancedUnverified = [];
    private static readonly int[] Temperatures = [4000, 5000, 6500, 7500, 8200, 9300, 10000, 11500];

    private static List<AdvancedControl> ReadAdvanced(IntPtr handle, out string report)
    {
        var controls = new List<AdvancedControl>();
        var capabilities = new Dictionary<byte, byte[]>();
        if (Native.GetCapabilitiesStringLength(handle, out uint length) && length is > 0 and <= 65536)
        {
            var buffer = new StringBuilder((int)length);
            if (Native.CapabilitiesRequestAndCapabilitiesReply(handle, buffer, length)) capabilities = DdcCapabilities.Parse(buffer.ToString());
        }
        if (capabilities.TryGetValue(0x87, out var sharpnessChoices) && Read(handle, 0x87, out uint maximum) is uint sharpness
            && (sharpnessChoices.Length == 0 || sharpnessChoices.Any(v => v == sharpness)))
            controls.Add(new(AdvancedKind.Sharpness, "Sharpness", sharpness, 0, maximum,
                sharpnessChoices.Select(v => new ControlChoice(v, v.ToString())).ToArray()));
        bool highLevel = Native.GetMonitorCapabilities(handle, out uint highCapabilities, out uint temperatures);
        report = $"VCP features: {string.Join(' ', capabilities.Keys.Order().Select(k => k.ToString("X2")))}; high-level={highLevel}; flags={highCapabilities:X}; temperatures={temperatures:X}";
        if (highLevel && temperatures != 0)
        {
            if (!Native.GetMonitorColorTemperature(handle, out uint temperature)) temperature = 0;
            var choices = Temperatures.Select((k, i) => (Kelvin: k, Index: i))
                .Where(t => (temperatures & (1u << t.Index)) != 0)
                .Select(t => new ControlChoice((uint)t.Kelvin, t.Kelvin + " K")).ToArray();
            uint kelvin = temperature is >= 1 and <= 8 ? (uint)Temperatures[temperature - 1] : 0;
            if (choices.Length > 0) controls.Add(new(AdvancedKind.Temperature, "Warmth", kelvin, 3000, 30000, choices));
        }
        if (!controls.Any(c => c.Kind == AdvancedKind.Temperature) && capabilities.ContainsKey(0x0b) && capabilities.ContainsKey(0x0c)
            && Native.GetVCPFeatureAndVCPFeatureReply(handle, 0x0b, IntPtr.Zero, out uint increment, out _)
            && Native.GetVCPFeatureAndVCPFeatureReply(handle, 0x0c, IntPtr.Zero, out uint units, out uint maximumUnits))
        {
            report += $"; temperature increment={increment}, current={units}, maximum={maximumUnits}";
            var choices = DdcCapabilities.TemperatureChoices(increment, units, maximumUnits);
            if (choices.Length > 1) controls.Add(new(AdvancedKind.Temperature, "Warmth", 3000 + units * increment, 3000, 30000, choices, increment));
        }
        foreach (var (kind, title, channel) in new[] { (AdvancedKind.Red, "Red balance", 0u), (AdvancedKind.Green, "Green balance", 1u), (AdvancedKind.Blue, "Blue balance", 2u) })
            if (Native.GetMonitorRedGreenOrBlueGain(handle, channel, out uint min, out uint current, out uint max)
                && min < max && current >= min && current <= max && max <= 65535)
                controls.Add(new(kind, title, current, min, max, []));
        if (capabilities.TryGetValue(0x72, out var gammaDescriptor))
        {
            var choices = DdcCapabilities.GammaChoices(gammaDescriptor);
            if (Native.GetVCPFeatureAndVCPFeatureReply(handle, 0x72, IntPtr.Zero, out uint gamma, out _))
            {
                report += $"; gamma descriptor={string.Join(' ', gammaDescriptor.Select(v => v.ToString("X2")))}, current={gamma:X4}";
                if (choices.Any(c => c.Value == gamma)) controls.Add(new(AdvancedKind.Gamma, "Gamma", gamma, 0, 65535, choices));
            }
        }
        return controls;
    }

    private AdvancedValues LatestAdvanced(Device device)
    {
        var values = new AdvancedValues();
        foreach (var control in device.Reading.Advanced ?? [])
        {
            uint value = advancedCurrent.GetValueOrDefault((device.Reading.Id, control.Kind), control.Current);
            if (control.Kind != AdvancedKind.Temperature || value > 0) values = values.SetRaw(control.Kind, value);
        }
        return values;
    }

    private void ApplyAdvanced(Device device, AdvancedValues values, bool force, List<string> errors)
    {
        foreach (var control in device.Reading.Advanced ?? [])
        {
            if (values.Get(control.Kind) is not uint requested) continue;
            if (values.Temperature.HasValue && control.Kind is AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue) continue;
            if (!control.Accepts(requested)) { errors.Add(control.Title + " value is no longer supported; refresh displays."); continue; }
            var key = (device.Reading.Id, control.Kind);
            if (!force && !advancedUnverified.Contains(key) && advancedCurrent.GetValueOrDefault(key, control.Current) == requested) continue;
            bool written;
            uint actual = uint.MaxValue;
            switch (control.Kind)
            {
                case AdvancedKind.Temperature:
                    if (control.TemperatureStep > 0)
                    {
                        written = Native.SetVCPFeature(device.Handle, 0x0c, (requested - 3000) / control.TemperatureStep);
                        if (written)
                        {
                            Thread.Sleep(50);
                            written = Native.GetVCPFeatureAndVCPFeatureReply(device.Handle, 0x0c, IntPtr.Zero, out uint units, out _);
                            actual = 3000 + units * control.TemperatureStep;
                        }
                    }
                    else
                    {
                        int index = Array.IndexOf(Temperatures, (int)requested);
                        written = index >= 0 && Native.SetMonitorColorTemperature(device.Handle, (uint)index + 1);
                        if (written)
                        {
                            written = Native.GetMonitorColorTemperature(device.Handle, out uint temperature);
                            actual = temperature is >= 1 and <= 8 ? (uint)Temperatures[temperature - 1] : 0;
                        }
                    }
                    // A temperature preset can change RGB gain. Read those values before later edits.
                    foreach (var rgb in (device.Reading.Advanced ?? []).Where(c => c.Kind is AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue))
                        if (Native.GetMonitorRedGreenOrBlueGain(device.Handle, (uint)(rgb.Kind - AdvancedKind.Red), out _, out uint gain, out _))
                            advancedCurrent[(device.Reading.Id, rgb.Kind)] = gain;
                    break;
                case AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue:
                    uint channel = (uint)(control.Kind - AdvancedKind.Red);
                    written = Native.SetMonitorRedGreenOrBlueGain(device.Handle, channel, requested);
                    if (written) written = Native.GetMonitorRedGreenOrBlueGain(device.Handle, channel, out _, out actual, out _);
                    advancedCurrent[(device.Reading.Id, AdvancedKind.Temperature)] = 0;
                    break;
                default:
                    byte code = control.Kind == AdvancedKind.Sharpness ? (byte)0x87 : (byte)0x72;
                    written = Native.SetVCPFeature(device.Handle, code, requested);
                    if (written) { Thread.Sleep(50); written = Native.GetVCPFeatureAndVCPFeatureReply(device.Handle, code, IntPtr.Zero, out actual, out _); }
                    break;
            }
            if (written && actual == requested) { advancedCurrent[key] = actual; advancedUnverified.Remove(key); }
            else { advancedUnverified.Add(key); errors.Add(control.Title + " could not be verified; check the monitor's picture mode."); }
        }
    }

    private static partial class Native
    {
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetCapabilitiesStringLength(IntPtr monitor, out uint length);
        [DllImport("dxva2.dll", CharSet = CharSet.Ansi, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool CapabilitiesRequestAndCapabilitiesReply(IntPtr monitor, StringBuilder capabilities, uint length);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorCapabilities(IntPtr monitor, out uint capabilities, out uint temperatures);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorColorTemperature(IntPtr monitor, out uint temperature);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetMonitorColorTemperature(IntPtr monitor, uint temperature);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool GetMonitorRedGreenOrBlueGain(IntPtr monitor, uint channel, out uint minimum, out uint current, out uint maximum);
        [DllImport("dxva2.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetMonitorRedGreenOrBlueGain(IntPtr monitor, uint channel, uint gain);
    }
}
