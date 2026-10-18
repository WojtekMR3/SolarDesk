namespace SolarDesk.Core;

/// <summary>Maps the driver-reported default to 50%, with desaturation below it.</summary>
public static class VibranceScale
{
    public static int ToRaw(int percent, int minimum, int neutral, int maximum)
    {
        Validate(minimum, neutral, maximum);
        double p = Math.Clamp(percent, 0, 100);
        double raw = p <= 50 ? minimum + (neutral - (double)minimum) * p / 50
            : neutral + (maximum - (double)neutral) * (p - 50) / 50;
        return (int)Math.Round(raw, MidpointRounding.AwayFromZero);
    }

    public static int ToPercent(int raw, int minimum, int neutral, int maximum)
    {
        Validate(minimum, neutral, maximum);
        double value = Math.Clamp(raw, minimum, maximum);
        double percent = value <= neutral ? 50 * (value - minimum) / (neutral - (double)minimum)
            : 50 + 50 * (value - neutral) / (maximum - (double)neutral);
        return (int)Math.Round(percent, MidpointRounding.AwayFromZero);
    }

    private static void Validate(int minimum, int neutral, int maximum)
    {
        if (!(minimum < neutral && neutral < maximum)) throw new ArgumentOutOfRangeException(nameof(neutral));
    }
}
