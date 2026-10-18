# Sunset Monitor

Your monitors, in sync with the sun. Sunset Monitor (formerly SolarDesk) is a Windows tray app with separate Day and Night profiles, smooth sunrise/sunset transitions, and controls for multiple displays.

<a href="https://github.com/WojtekMR3/SolarDesk/releases/download/v2.9.6/SolarDesk-2.9.6-win-x64.zip">
  <img src="docs/download-windows.svg" alt="Download for Windows" width="280" height="60">
</a>

**Free · Windows 10 / 11 (64-bit) · Version 2.9.6**

Click the button to download the ZIP. Right-click the downloaded file, choose **Extract All**, then open **SunsetMonitor.exe** inside the extracted folder.

[Release notes](https://github.com/WojtekMR3/SunsetMonitor/releases/latest) · [User guide](docs/user-guide.md) · [Report a bug](https://github.com/WojtekMR3/SunsetMonitor/issues)

| Day | Night |
| --- | --- |
| ![Day profile with warm colors and three monitor cards](docs/screenshots/day.png) | ![Night profile with a dark sky and three monitor cards](docs/screenshots/night.png) |

Screenshots use simulated monitors and a demo location.

## Install

1. Click **[Download for Windows](https://github.com/WojtekMR3/SolarDesk/releases/download/v2.9.6/SolarDesk-2.9.6-win-x64.zip)** to download the app directly.
2. Right-click the ZIP and choose **Extract All**. Open the extracted `SunsetMonitor` folder and double-click `SunsetMonitor.exe`.
3. Enable **DDC/CI** in your monitor's on-screen menu if needed.
4. Choose a city or use Windows location, then set your Day and Night values.

Requires **Windows 10 version 2004 or newer / Windows 11, x64**, and a compatible external monitor. The portable download includes .NET; no separate runtime installation or administrator access is needed. Current builds are unsigned, so Windows may show an unknown-publisher or SmartScreen warning.

Closing the window keeps Sunset Monitor in the system tray. Use the tray menu to quit. **Start with Windows** is optional in Settings.

Sunset Monitor works when opened normally or started in the tray. **Start with Windows** is off by default; enabling it launches Sunset Monitor quietly in the tray when you sign in. The `--tray` option only hides the initial window and is not required for monitor controls or automatic scheduling.

**Single-click the tray icon** for three vertical brightness, contrast, and saturation bars. Drag upward to increase a value. Switch displays with numbered monitor icons arranged left to right like your Windows desktop, for example **2 / 1 / 3**. The main display stays in its actual position. Each icon reflects its aspect ratio and shows a label such as **16:9** or **21:9**; **MAIN** marks Windows' primary display. The selected monitor's name appears below them. Choose Day/Night, toggle **Auto** beside the profile buttons, or use Mass and Offset modes for linked adjustments. Auto stays synchronized with the main window: on follows the sun, off keeps the selected profile active. Changes apply and save automatically. Click elsewhere or press **Esc** to dismiss the panel; double-click the icon or choose **Open full settings** to open the main window.

The panel shows **Detecting displays…** while startup discovery is running. If values remain unavailable, use **Refresh displays** directly in the tray panel. Opening the panel also retries missing or unreadable displays, with a short cooldown between automatic retries.

## Features

- **Tray quick controls** for brightness, contrast, saturation, profiles, and display modes with one click.
- Separate **Day** and **Night** brightness, contrast, saturation, and supported picture settings.
- **Auto** follows local sunrise and sunset, including catching up at startup and after wake. With Auto off, the selected profile stays active.
- **Individual**, **Mass**, and **Offset** controls, each with saved Day/Night values. Offset preserves differences between displays.
- Monitor cards show resolution, aspect ratio, desktop order, and the main display.
- Additional hardware controls such as sharpness, warmth, RGB balance, and gamma when supported; saved presets and hotkeys.
- Offline city search and sun calculations. Optional weather, background effects, and sunrise/sunset reminders.

## Background performance

Sunset Monitor stops decorative animations and weather requests when another app has focus, the window is minimized, or it is in the tray. Idle background checks run once per minute; automatic fades still update every ten seconds, and slider edits apply immediately. Periodic monitor rescans wait until the full window is in use; display changes and wake events still trigger discovery.

**Start with Windows** launches directly in the tray without rendering the full window. See [performance measurements and benchmark instructions](docs/performance.md) for measured CPU and memory use and the test limitations.

## Monitor support

Brightness and contrast use DDC/CI. Support varies by monitor, connection, dock, HDR mode, and picture preset. Unsupported controls are disabled. Built-in laptop brightness is not implemented.

Saturation uses monitor hardware when available, with an NVIDIA Digital Vibrance fallback on compatible drivers. Advanced controls appear only when usable support is detected. NVIDIA fallback uses a private driver interface, so compatibility may change between driver versions.

If a monitor is missing, enable DDC/CI and choose **Refresh displays**. Avoid running multiple automatic monitor controllers at once. See the [user guide](docs/user-guide.md) for diagnostics and restoration.

## Privacy

Settings and logs stay in `%LOCALAPPDATA%\SolarDesk`. Sunset Monitor has no analytics, account, or automatic update check. Sun calculations and city search work offline. Optional weather sends coordinates rounded to one decimal place to Open-Meteo; weather is enabled by default after location setup and can be disabled in Settings. [Privacy details](docs/privacy.md).

## Update or remove

Quit Sunset Monitor from its tray menu before replacing the extracted files. Keep the same folder if **Start with Windows** is enabled. Settings are stored separately and survive updates.

**Upgrading from SolarDesk:** quit the old app, extract the new download, then run `SunsetMonitor.exe`. Your existing profiles and preferences load automatically. Enabled startup moves to the new executable on first launch; disabled startup stays off. Update any shortcuts to point to `SunsetMonitor.exe`. The settings folder retains its original `SolarDesk` name for compatibility.

To remove it, disable **Start with Windows**, quit, and delete its folder. Delete `%LOCALAPPDATA%\SolarDesk` separately to erase saved settings. Quitting does not reset the monitor; use the app's restore control first if you want the original hardware values.

## Build

Install a **.NET 8 SDK (8.0.4xx)** on Windows, then run:

```powershell
.\test.ps1                     # Core tests only; no windows or hardware writes
.\build.ps1                    # Standalone executable in dist\SunsetMonitor
.\package.ps1                  # Tested release ZIP in GitHub Actions
```

The first build needs NuGet access for Microsoft runtime and Windows targeting packages. There are no third-party NuGet library dependencies. A project-local SDK in `.tools\dotnet` is used when present; otherwise the scripts use `dotnet` from PATH. UI checks open and focus test windows, so run them in GitHub Actions. On a dedicated test desktop only, `test.ps1 -IncludeUi` and `package.ps1 -IncludeUi` opt into those checks. [Release process](docs/releasing.md).

## License and credits

Sunset Monitor is [MIT licensed](LICENSE). City data comes from GeoNames; optional weather comes from Open-Meteo. Their data uses CC BY 4.0. Bundled Microsoft runtime components retain their own licenses. See [third-party notices](THIRD-PARTY-NOTICES.md) and the [API references](docs/user-guide.md#api-and-calculation-references).
