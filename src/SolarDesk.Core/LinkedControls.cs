namespace SolarDesk.Core;

public enum MonitorLinkMode { Individual, SameValues, KeepOffsets }

public static class LinkedControls
{
    public static (int Offset, int[] Values) ApplyOffset(IReadOnlyList<int> baselines, int requestedOffset)
    {
        if (baselines.Count == 0) return (0, []);
        int[] values = baselines.Select(value => Math.Clamp(value, 0, 100)).ToArray();
        int offset = Math.Clamp(requestedOffset, -values.Min(), 100 - values.Max());
        return (offset, values.Select(value => value + offset).ToArray());
    }

    public static int[] Apply(IReadOnlyList<int> values, int sourceIndex, int requestedValue, MonitorLinkMode mode)
    {
        if (sourceIndex < 0 || sourceIndex >= values.Count) throw new ArgumentOutOfRangeException(nameof(sourceIndex));
        int requested = Math.Clamp(requestedValue, 0, 100);
        int[] result = values.Select(value => Math.Clamp(value, 0, 100)).ToArray();
        if (mode == MonitorLinkMode.SameValues) Array.Fill(result, requested);
        else if (mode == MonitorLinkMode.KeepOffsets)
        {
            // Limit the whole group together so differences survive reaching either endpoint.
            int delta = Math.Clamp(requested - result[sourceIndex], -result.Min(), 100 - result.Max());
            for (int i = 0; i < result.Length; i++) result[i] += delta;
        }
        else result[sourceIndex] = requested;
        return result;
    }
}
