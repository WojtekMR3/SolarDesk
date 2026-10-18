using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SolarDesk;

internal static class TrayArtwork
{
    public static Icon Create()
    {
        using var bitmap = new Bitmap(64, 64);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var dark = new SolidBrush(Color.FromArgb(20, 29, 43));
        using var gold = new SolidBrush(Color.FromArgb(244, 182, 107));
        using var ray = new Pen(gold, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        graphics.FillEllipse(dark, 1, 1, 62, 62);
        graphics.FillEllipse(gold, 20, 20, 24, 24);
        for (int i = 0; i < 8; i++)
        {
            double angle = Math.PI * i / 4;
            graphics.DrawLine(ray, 32 + (float)Math.Cos(angle) * 19, 32 + (float)Math.Sin(angle) * 19,
                32 + (float)Math.Cos(angle) * 24, 32 + (float)Math.Sin(angle) * 24);
        }
        IntPtr handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { DestroyIcon(handle); }
    }
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
}
