using System.Text.Json;
using System.Windows;

namespace SolarDesk;

public partial class App : Application
{
    private Mutex? singleInstance;
    private EventWaitHandle? activateEvent;
    private RegisteredWaitHandle? activateWait;
    private bool ownsMutex;

    protected override async void OnStartup(StartupEventArgs e)
    {
        // Avoid partially unpainted surfaces on multi-monitor GPU/driver configurations.
        // This small control panel uses software composition; monitor control is unaffected.
        System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        base.OnStartup(e);
        DispatcherUnhandledException += (_, args) =>
        {
            Log(args.Exception);
            if (MainWindow is MainWindow window) window.ReportError(args.Exception);
            args.Handled = true;
        };
        if (e.Args.Contains("--location-api-check"))
        {
            string output = Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "location-api-check.txt");
            try
            {
                bool available = LocationService.IsWindowsApiAvailable();
                File.WriteAllText(output, available ? "PASS Windows Geolocator.RequestAccessAsync is available. No permission or location was requested." : "FAIL Windows location API is unavailable.");
                Shutdown(available ? 0 : 1);
            }
            catch (Exception ex) { File.WriteAllText(output, ex.ToString()); Shutdown(1); }
            return;
        }
        if (e.Args.Contains("--probe") || e.Args.Contains("--verify-ddc"))
        {
            string output = Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "monitor-diagnostics.json");
            try
            {
                await using var monitors = new MonitorService();
                var readings = await monitors.ScanAsync();
                var verification = e.Args.Contains("--verify-ddc") ? await monitors.VerifyCurrentValuesAsync() : [];
                File.WriteAllText(output, JsonSerializer.Serialize(new { Monitors = readings, Verification = verification }, new JsonSerializerOptions { WriteIndented = true }));
                Shutdown(0);
            }
            catch (Exception ex) { File.WriteAllText(output, ex.ToString()); Shutdown(1); }
            return;
        }
        string? render = Argument(e.Args, "--render");
        bool smoke = e.Args.Contains("--ui-smoke");
        bool performance = e.Args.Contains("--performance-check");
        bool diagnostic = render is not null || smoke || performance;
        if (!diagnostic)
        {
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName;
            string name = "Local\\SolarDesk-" + user;
            singleInstance = new Mutex(true, name, out ownsMutex);
            activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-activate");
            if (!ownsMutex) { activateEvent.Set(); Shutdown(); return; }
        }
        var main = new MainWindow(diagnostic);
        if (performance) main.ShowActivated = false;
        MainWindow = main;
        if (!diagnostic)
            activateWait = ThreadPool.RegisterWaitForSingleObject(activateEvent!, (_, _) => Dispatcher.BeginInvoke(main.ShowFromTray), null, -1, false);
        bool tray = !diagnostic && e.Args.Contains("--tray");
        if (!tray && !performance) main.Show();
        try
        {
            await main.InitializeAsync();
            if (diagnostic)
            {
                if (performance) await main.RunPerformanceCheckAsync(Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "performance.json"),
                    int.TryParse(Argument(e.Args, "--seconds"), out int seconds) ? Math.Clamp(seconds, 15, 600) : 60);
                if (smoke) await main.RunUiSmokeAsync(Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "ui-smoke.txt"));
                if (render is not null) await main.RenderAsync(render);
                await main.ExitAsync();
            }
            else
            {
                main.PromptLocationOnFirstLaunch(tray);
            }
        }
        catch (Exception ex)
        {
            if (!diagnostic) { main.ReportError(ex); return; }
            // Remote UI checks need the actual assertion, not only a nonzero exit code.
            try { File.WriteAllText(Argument(e.Args, "--output") ?? Path.Combine(AppContext.BaseDirectory, "diagnostic-error.txt"), ex.ToString()); }
            finally { Shutdown(1); }
        }
    }

    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SolarDesk");
    public static string VersionLabel => $"SolarDesk {typeof(App).Assembly.GetName().Version?.ToString(3)}";
    public static string? Argument(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
    public static void Log(Exception error)
    {
        try
        {
            Directory.CreateDirectory(DataDirectory);
            string file = Path.Combine(DataDirectory, "errors.log");
            if (File.Exists(file) && new FileInfo(file).Length > 1_000_000) File.Move(file, file + ".old", true);
            File.AppendAllText(file, DateTimeOffset.Now.ToString("O") + " " + error + Environment.NewLine);
        }
        catch { }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        activateWait?.Unregister(null);
        activateEvent?.Dispose();
        if (ownsMutex) singleInstance?.ReleaseMutex();
        singleInstance?.Dispose();
        base.OnExit(e);
    }
}
