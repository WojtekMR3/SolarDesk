using System.Globalization;
using System.Text.RegularExpressions;

namespace SolarDesk.Core;

public enum AdvancedKind { Sharpness, Temperature, Red, Green, Blue, Gamma }
public sealed record ControlChoice(uint Value, string Label)
{
    public override string ToString() => Label;
}
public sealed record AdvancedControl(AdvancedKind Kind, string Title, uint Current, uint Minimum, uint Maximum, ControlChoice[] Choices, uint TemperatureStep = 0)
{
    public bool Accepts(uint value) => Choices.Length > 0 ? Choices.Any(c => c.Value == value) : value >= Minimum && value <= Maximum;
}

public sealed record AdvancedValues(uint? Sharpness = null, uint? Temperature = null, uint? Red = null,
    uint? Green = null, uint? Blue = null, uint? Gamma = null)
{
    public uint? Get(AdvancedKind kind) => kind switch
    {
        AdvancedKind.Sharpness => Sharpness, AdvancedKind.Temperature => Temperature,
        AdvancedKind.Red => Red, AdvancedKind.Green => Green, AdvancedKind.Blue => Blue, AdvancedKind.Gamma => Gamma,
        _ => null
    };
    public AdvancedValues SetRaw(AdvancedKind kind, uint value) => kind switch
    {
        AdvancedKind.Sharpness => this with { Sharpness = value },
        AdvancedKind.Temperature => this with { Temperature = value },
        AdvancedKind.Red => this with { Red = value },
        AdvancedKind.Green => this with { Green = value },
        AdvancedKind.Blue => this with { Blue = value },
        AdvancedKind.Gamma => this with { Gamma = value }, _ => this
    };
    public AdvancedValues Set(AdvancedKind kind, uint value)
    {
        var next = SetRaw(kind, value);
        return kind == AdvancedKind.Temperature ? next with { Red = null, Green = null, Blue = null }
            : kind is AdvancedKind.Red or AdvancedKind.Green or AdvancedKind.Blue ? next with { Temperature = null } : next;
    }
    public bool IsValid() => Enum.GetValues<AdvancedKind>().All(k => Get(k) is null or <= 65535)
        && Temperature is null or >= 3000 and <= 30000;
    public static AdvancedValues Capture(IEnumerable<AdvancedControl> controls)
    {
        var result = new AdvancedValues();
        foreach (var control in controls.OrderBy(c => c.Kind == AdvancedKind.Temperature ? 1 : 0))
            if (control.Kind != AdvancedKind.Temperature || control.Current > 0)
            result = result.Set(control.Kind, control.Current);
        return result;
    }
}

public sealed class MonitorPreset
{
    public string Name { get; set; } = "Preset";
    public ControlValues Values { get; set; } = new(50, 50, 50);
    public AdvancedValues Advanced { get; set; } = new();
    public int Hotkey { get; set; }
    public override string ToString() => Name;
}

public static class DdcCapabilities
{
    public static Dictionary<byte, byte[]> Parse(string text)
    {
        var result = new Dictionary<byte, byte[]>();
        var start = Regex.Match(text, @"\bvcp\s*\(", RegexOptions.IgnoreCase);
        if (!start.Success) return result;
        int depth = 1, end = start.Index + start.Length;
        for (; end < text.Length && depth > 0; end++)
        { if (text[end] == '(') depth++; else if (text[end] == ')') depth--; }
        if (depth != 0) return result;
        string section = text.Substring(start.Index + start.Length, end - start.Index - start.Length - 1);
        for (int index = 0; index < section.Length;)
        {
            while (index < section.Length && char.IsWhiteSpace(section[index])) index++;
            int tokenStart = index;
            while (index < section.Length && !char.IsWhiteSpace(section[index]) && section[index] is not ('(' or ')')) index++;
            string token = section[tokenStart..index];
            while (index < section.Length && char.IsWhiteSpace(section[index])) index++;
            string choices = "";
            if (index < section.Length && section[index] == '(')
            {
                int choicesStart = ++index, choicesDepth = 1;
                while (index < section.Length && choicesDepth > 0)
                {
                    if (section[index] == '(') choicesDepth++;
                    else if (section[index] == ')') choicesDepth--;
                    index++;
                }
                if (choicesDepth != 0) return [];
                choices = section[choicesStart..(index - 1)];
            }
            else if (index < section.Length && section[index] == ')') index++;
            // Consume each entire value group, even if malformed. Its options are never feature codes.
            if (token.Length != 2 || !byte.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte code)) continue;
            string[] values = choices.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (values.Any(v => !byte.TryParse(v, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _))) continue;
            result[code] = values.Select(v => byte.Parse(v, NumberStyles.HexNumber)).ToArray();
        }
        return result;
    }

    // MCCS gamma descriptors encode mode, native gamma, then a range or explicit choices.
    // Absolute gamma is 1 + SH / 100, with SL=0; relative white adjustment uses SL=4.
    public static ControlChoice[] GammaChoices(byte[] descriptor)
    {
        if (descriptor.Length is >= 2 and <= 16 && descriptor[0] != 0xff
            && (descriptor.Length < 3 || descriptor[2] < 0xfa) && descriptor.Distinct().Count() == descriptor.Length)
            return descriptor.Select((v, i) => new ControlChoice(v, "Mode " + (i + 1))).ToArray();
        if (descriptor.Length < 3) return [];
        bool relative = descriptor[0] == 0xff;
        if (!relative && descriptor[0] > 10) return [];
        int[] possible = relative ? new[] { 0 }.Concat(Enumerable.Range(1, 10)).Concat(Enumerable.Range(17, 10)).ToArray()
            : Enumerable.Range(0, 256).ToArray();
        int Relative(int code) => code == 0 ? 0 : code <= 10 ? -code : code - 16;
        IEnumerable<int> allowed;
        switch (descriptor[2])
        {
            case 0xff or 0xfe when descriptor.Length == 3: allowed = possible; break;
            case 0xfd or 0xfc when descriptor.Length == 5:
                int low = relative ? Relative(descriptor[3]) : descriptor[3];
                int high = relative ? Relative(descriptor[4]) : descriptor[4];
                if (!possible.Contains(descriptor[3]) || !possible.Contains(descriptor[4]) || low > high) return [];
                allowed = possible.Where(n => (relative ? Relative(n) : n) >= low && (relative ? Relative(n) : n) <= high); break;
            case 0xfb or 0xfa when descriptor.Length > 3: allowed = descriptor.Skip(3).Select(n => (int)n).Where(possible.Contains); break;
            default: return [];
        }
        return allowed.Distinct().OrderBy(n => relative ? Relative(n) : n).Select(n => new ControlChoice(
            (uint)((n << 8) | (relative ? 4 : 0)), relative
                ? n == 0 ? "Native" : "Native " + (Relative(n) / 10d).ToString("+0.0;-0.0", CultureInfo.InvariantCulture)
                : (1 + n / 100d).ToString("0.00", CultureInfo.InvariantCulture))).ToArray();
    }

    public static ControlChoice[] TemperatureChoices(uint increment, uint current, uint maximum)
    {
        ulong kelvin = 3000UL + (ulong)current * increment;
        if (increment is 0 or > 1000 || maximum == 0 || current > maximum || kelvin is < 3000 or > 30000) return [];
        return new uint[] { 3500, 4000, 4500, 5000, 5500, 6000, 6500, 7500, 8200, 9300, 10000, 11500 }
            .Append((uint)kelvin).Where(k => (k - 3000) % increment == 0 && (k - 3000) / increment <= maximum)
            .Distinct().Order().Select(k => new ControlChoice(k, k + " K")).ToArray();
    }
}
