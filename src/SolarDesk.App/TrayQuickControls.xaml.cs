using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Forms = System.Windows.Forms;

namespace SolarDesk;

public partial class TrayQuickControls : Window
{
    private bool closingForExit;
    private const uint SwpNoSize = 0x0001, SwpNoActivate = 0x0010;
    internal Func<bool, Task>? SelectProfile { get; set; }
    internal Action? SelectMonitor { get; set; }
    internal Action? OpenSettings { get; set; }

    public TrayQuickControls()
    {
        InitializeComponent();
        Deactivated += (_, _) => Hide();
        Closing += (_, e) => { if (!closingForExit) { e.Cancel = true; Hide(); } };
    }

    internal void ShowNearTray()
    {
        var pointer = Forms.Cursor.Position;
        var work = Forms.Screen.FromPoint(pointer).WorkingArea;
        var handle = new WindowInteropHelper(this).EnsureHandle();
        // Move onto the target display before sizing in DIPs; Windows then supplies that display's DPI.
        SetWindowPos(handle, new IntPtr(-1), work.Right - 1, work.Bottom - 1, 0, 0, SwpNoSize | SwpNoActivate);
        double scale = Math.Max(96, GetDpiForWindow(handle)) / 96.0;
        MaxHeight = Math.Max(100, (work.Height - 16) / scale);
        MaxWidth = Math.Max(100, (work.Width - 16) / scale);
        Show();
        UpdateLayout();
        if (GetWindowRect(handle, out var bounds))
        {
            int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
            var position = FitToWorkArea(pointer, work, width, height);
            SetWindowPos(handle, new IntPtr(-1), position.X, position.Y, 0, 0, SwpNoSize);
        }
        Activate();
        BrightnessSlider.Focus();
    }

    internal static System.Drawing.Point FitToWorkArea(System.Drawing.Point pointer, System.Drawing.Rectangle work, int width, int height)
        => new(Math.Clamp(pointer.X - width + 24, work.Left + 8, Math.Max(work.Left + 8, work.Right - width - 8)),
            Math.Clamp(pointer.Y - height - 12, work.Top + 8, Math.Max(work.Top + 8, work.Bottom - height - 8)));

    internal void CloseForExit() { closingForExit = true; Close(); }
    private void QuickKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { e.Handled = true; Hide(); }
    }
    private void DismissClick(object sender, RoutedEventArgs e) => Hide();
    private async void DayClick(object sender, RoutedEventArgs e) { if (SelectProfile is not null) await SelectProfile(false); }
    private async void NightClick(object sender, RoutedEventArgs e) { if (SelectProfile is not null) await SelectProfile(true); }
    private void MonitorChanged(object sender, SelectionChangedEventArgs e) => SelectMonitor?.Invoke();
    private void OpenClick(object sender, RoutedEventArgs e) { Hide(); OpenSettings?.Invoke(); }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
}
