namespace SolarDesk.Core;

public readonly record struct DisplayShape(int Width, int Height)
{
    public bool IsKnown => Width > 0 && Height > 0;
    public double Ratio => IsKnown ? (double)Width / Height : 16d / 9;
    public string Resolution => IsKnown ? $"{Width} × {Height}" : "Resolution unavailable";
    public string Aspect
    {
        get
        {
            if (!IsKnown) return "—";
            bool portrait = Width < Height;
            double ratio = portrait ? 1 / Ratio : Ratio;
            (int W, int H)[] common = [(32, 9), (21, 9), (16, 9), (16, 10), (3, 2), (4, 3), (5, 4), (1, 1)];
            foreach (var (w, h) in common)
                if (Math.Abs(ratio / ((double)w / h) - 1) < (w == 21 ? 0.03 : 0.005))
                    return portrait ? $"{h}:{w}" : $"{w}:{h}";
            int a = Width, b = Height;
            while (b != 0) (a, b) = (b, a % b);
            return $"{Width / a}:{Height / a}";
        }
    }
}
