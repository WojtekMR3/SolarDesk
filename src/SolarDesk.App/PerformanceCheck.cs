using System.Diagnostics;
using System.Text.Json;

namespace SolarDesk;

public partial class MainWindow
{
    private bool? diagnosticForeground;
    private int diagnosticCycleCount, diagnosticScanCount;

    // This mode shares the application scheduler and renderer, but never accesses
    // real monitors, saved settings, hotkeys, notifications, or the weather network.
    public async Task RunPerformanceCheckAsync(string output, int seconds)
    {
        if (!diagnostic) throw new InvalidOperationException("Performance checks require demo mode.");
        var phases = new List<object>();
        diagnosticForeground = false;
        Hide();
        _ = new System.Windows.Interop.WindowInteropHelper(this).EnsureHandle();
        timer.Start();
        await MeasureAsync("tray-startup");
        settings.DayRaysEnabled = true;
        settings.WeatherEnabled = settings.WeatherEffectsEnabled = true;
        weather = new WeatherSnapshot(14, 61, true, DateTimeOffset.UtcNow);
        SelectProfileEditor(true);
        Show();
        UpdateStarMotion(); UpdateSunRayMotion(); UpdateWeatherEffects();
        await MeasureAsync("inactive-window-night-rain");
        Hide();
        await MeasureAsync("tray-after-window");
        timer.Stop();

        async Task MeasureAsync(string name)
        {
            await Task.Delay(2000);
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            TimeSpan cpuStart = process.TotalProcessorTime;
            long allocatedStart = GC.GetTotalAllocatedBytes();
            int cycles = diagnosticCycleCount, scans = diagnosticScanCount;
            var elapsed = Stopwatch.StartNew();
            var working = new List<long>();
            var committed = new List<long>();
            int maxHandles = 0;
            while (elapsed.Elapsed.TotalSeconds < seconds)
            {
                await Task.Delay(1000);
                process.Refresh();
                working.Add(process.WorkingSet64);
                committed.Add(process.PrivateMemorySize64);
                maxHandles = Math.Max(maxHandles, process.HandleCount);
            }
            double duration = elapsed.Elapsed.TotalSeconds;
            double cpuMs = (process.TotalProcessorTime - cpuStart).TotalMilliseconds;
            phases.Add(new
            {
                Name = name, Seconds = Math.Round(duration, 2),
                CpuMilliseconds = Math.Round(cpuMs, 2),
                CpuPercentOfMachine = Math.Round(cpuMs / (duration * 10 * Environment.ProcessorCount), 4),
                CpuPercentOfOneCore = Math.Round(cpuMs / (duration * 10), 3),
                AverageWorkingSetMiB = Math.Round(working.Average() / 1048576, 2),
                PeakWorkingSetMiB = Math.Round(working.Max() / 1048576.0, 2),
                AveragePrivateCommitMiB = Math.Round(committed.Average() / 1048576, 2),
                AllocatedBytes = GC.GetTotalAllocatedBytes() - allocatedStart,
                Cycles = diagnosticCycleCount - cycles, Scans = diagnosticScanCount - scans,
                AnimationClocksActive = starMotionRunning || sunRayMotionRunning || WeatherParticles.Children.Count > 0,
                MaxHandles = maxHandles
            });
            File.WriteAllText(output, JsonSerializer.Serialize(new
            {
                Version = App.VersionLabel, LogicalProcessors = Environment.ProcessorCount,
                SimulatedMonitors = monitors.Count, Phases = phases
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
