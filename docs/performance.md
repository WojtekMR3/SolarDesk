# Background performance

Sunset Monitor is designed to stay quiet while another application or game has focus:

- Stars, sun rays, weather particles, and theme transitions stop when the main window loses focus, is minimized, or is hidden. They resume when the window is active again.
- Weather requests wait until the main window is active. Unfinished requests are cancelled when it goes into the background.
- The background scheduler normally wakes once per minute. During an automatic fade it updates every ten seconds, with shorter waits for transition boundaries, override expiry, and reminder delivery. Slider changes retain their existing 100 ms debounce.
- Periodic five-minute monitor rescans run only while the full window is active. Display-change events coalesce into a refresh after about one second; resume from sleep and explicit refresh also discover monitors. If a monitor's own power switch does not generate a Windows display event, open the full window or use **Refresh displays**.
- **Start with Windows** uses `--tray`, which initializes monitor control and preset shortcuts without first showing and rendering the full window. The compact tray sliders remain available.

The standalone executable leaves embedded assemblies uncompressed; the download ZIP provides compression. This avoids decompressing assemblies into private memory at startup. The extracted executable is larger, while the user still downloads one ZIP and launches one executable. Microsoft's [single-file deployment documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview#compress-assemblies-in-single-file-apps) describes this tradeoff.

## Measured comparison: 2.9.0 to 2.9.1

Measured on 18 October 2026, on the same Windows PC with 16 logical processors, using the standalone x64 builds, three simulated monitors, and 60 seconds per phase. The 2.9.0 baseline was commit `2b604a9` with only the diagnostic harness and counters added. No other Sunset Monitor instance or build/test job ran during either measurement. The 2.9.1 build uses the uncompressed executable described above.

| Scenario | 2.9.0 CPU | 2.9.1 CPU | 2.9.0 average working set | 2.9.1 average working set |
| --- | ---: | ---: | ---: | ---: |
| Tray startup | 0.145% | 0.089% | 246.40 MiB | 94.00 MiB |
| Inactive Night window, rain enabled | 0.648% | 0.078% | 288.15 MiB | 121.01 MiB |
| Back in tray after opening the window | 0.171% | 0.067% | 290.08 MiB | 120.43 MiB |

CPU is the average percentage of this PC's total logical CPU capacity. For example, the inactive-window result is 10.366% of one core before and 1.248% afterward. The sampler is included in these numbers. This is a short controlled comparison, not a long-duration memory-leak test.

Average private committed memory was 141.64 / 178.56 / 179.93 MiB before and 55.71 / 62.62 / 61.62 MiB afterward, in table order. Each phase ran six scheduler cycles before and one afterward, with no periodic rescans. In the inactive-window phase, managed allocation fell from 128,102,464 bytes to 287,608 bytes; decorative animation clocks were stopped in all three new-build phases.

The executable grew from approximately 75 MiB to 180 MiB on disk. This is a disk-space tradeoff, not an increase in measured resident RAM.

## Reproduce the measurement

Use a dedicated test desktop or an isolated Windows environment. This benchmark shows a demo window; it must not run on someone's active working desktop. Build the standalone executable, close other Sunset Monitor instances, and run:

```powershell
.\build.ps1
.\benchmark.ps1 -Seconds 60 -IncludeUi
```

For a different executable or output path:

```powershell
.\benchmark.ps1 -Executable .\dist\SunsetMonitor\SunsetMonitor.exe -Seconds 60 -Output .\artifacts\performance.json -IncludeUi
```

The benchmark spends the requested number of seconds in each of three phases, with a two-second settling period before each: hidden tray startup, an inactive visible Night window with rain effects enabled, and hidden again after opening the window. It shows a demo window without requesting foreground activation. Foreground state is held inactive inside the diagnostic harness so focus changes elsewhere cannot invalidate the comparison.

It uses three simulated monitors and simulated weather. It does not read or write saved settings, write to real monitors, register preset shortcuts, issue notifications, or contact the weather service. It uses the application scheduler and rendering paths. There is no forced garbage collection or artificial working-set trimming.

Reports include elapsed CPU time, percentage of total logical CPU capacity, percentage of one core, working set, private committed memory, managed allocations, scheduler cycles, scans, and handle counts. Working set includes resident shared/file-backed pages; private committed memory is a different measure and is not added to working set. The in-process sampler's overhead is included.

These are controlled application measurements, not game FPS or frame-time tests. CPU, memory, hardware calls, startup, and foreground rendering vary by PC, display count, monitor firmware, and workload. There is no fixed RAM cap or guarantee of zero game impact.
