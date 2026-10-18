# SolarDesk

A Windows tray app that adjusts each monitor's hardware brightness and contrast around local sunrise and sunset.

## Start

Run `dist\SolarDesk\SolarDesk.exe`. The standalone Windows x64 build includes its runtime and does not require installation or administrator access.

1. On the first visible launch, click **Use my location** and allow access in the Windows prompt. SolarDesk saves the location and shows a short city name. **Choose a city instead** works offline when you prefer manual setup or Windows location is unavailable. **Not now** goes straight to the sliders and prevents repeated startup prompts. Click the location chip to return to setup.
2. Choose **Day** or **Night** to apply and edit that profile's saved brightness, contrast, saturation, and supported additional controls. Both tabs remain available whether Auto is on or off. With **Auto off**, SolarDesk keeps the selected profile until you switch, including across restarts. Turning Auto off holds the currently selected Day or Night values. There is no separate Manual tab or profile editor. Each profile keeps independent Individual, Mass, and Offset values.
3. Turn on **Auto** to use saved Day and Night profiles around sunrise and sunset. Startup, wake, and enabling Auto evaluate the current sun position immediately after discovering the displays: opening at 15:00 during daylight applies Day even if the app last ran at 02:00. Choosing **Day** or **Night** immediately applies that profile and opens it for editing without turning Auto off. Slider and preset edits stay active until the next scheduled fade begins, rather than expiring after one hour. An edit made during a fade stays active until that fade ends. **Follow sun now** catches up immediately. Startup does not restore a temporary edit override. Auto selects the matching editor at the transition midpoint. If Auto is already off, selecting Day/Night leaves it off. Location setup never enables Auto unless you requested it.
4. Open the gear icon for fade length, timing offsets, startup, monitor inclusion, refresh, and restore. **Start with Windows** adds a current-user startup entry; no background service is installed. Quiet tray startup does not interrupt with setup.

The 30-minute default fade begins 15 minutes before sunrise/sunset and ends 15 minutes after it. Fade length and optional event offsets are in **Settings**. A zero-minute fade switches immediately. The top row groups city, local weather, sunrise and sunset. The lower schedule strip shows the next transition. Sun times are calculated locally, before any schedule offsets.

The background follows the selected profile: **Day** shows the warm daytime gradient and sun; **Night** shows the dark sky, moon, and stars. The former independent appearance override has been removed, so it cannot keep the Day profile dark. Day uses warm cream cards and an apricot-orange main-display card with a deeper copper border over a slightly deeper butter-yellow, blush-pink, and peach gradient, with dark text, warm sliders, and a small background sun surrounded by short, thin golden rays. The rays slowly brighten and drift outward a few pixels, stay within a compact corner of the window, and can be disabled in Settings. Motion pauses in Night mode, while hidden or minimized, and when Windows animations are disabled. Night uses deep blue and a crescent moon. Outlined Day/Night buttons make the active profile clear. Night's soft, round stars drift a few pixels and twinkle through independent, irregular sequences with occasional pauses. Motion stops in Day mode and while the window is hidden or minimized, and respects Windows' animation setting. The sun and moon rise and set inside the header badge while the sky glow shifts when switching profiles. Text and surface colors switch together to maintain contrast between the light Day and dark Night themes.

Displays appear left-to-right in Windows desktop order, with their Windows display numbers. The main display has a slightly larger, tinted card, a thicker accent border and a MAIN DISPLAY badge. Monitor icons follow each active display resolution: ultrawide screens look wider and portrait screens taller. Resolution and familiar aspect families (such as 16:9 or 21:9) appear below the monitor name; these describe the current Windows mode, not the physical panel size. Common ultrawide resolutions are grouped under the familiar 21:9 label. Unsupported controls remain visible but disabled. **Follow sun now** ends a profile preview early when automatic scheduling is enabled; **Resume auto** ends a tray pause. Arrow keys adjust focused sliders one step at a time; all three controls increase toward the top. The saturation bar becomes greyer as its value falls and more colorful as it rises. In Offset mode its color follows the supported displays' average resulting saturation, so zero offset retains the baseline's intensity. Day uses deeper burnt orange for contrast; Night uses periwinkle brightness and contrast bars. The mode menu explains each option and marks the selected mode.

The selector beside the profile tabs controls how displays are linked:

- **Individual** changes only the display you edit.
- **Mass mode** shows one compact monitor panel with three vertical sliders: brightness, contrast, and saturation. Its display-number label identifies the connected group; the individual monitor cards are hidden. A slider sets the same value on every connected display that supports that control. Different values are labeled **Mixed** until you adjust them; the thumb initially uses the main display's value. **Match main display** copies all three main-display values in one click.
- **Offset mode** uses the same single monitor panel with three vertical sliders centered on **0**. A center mark and signed labels make positive and negative offsets visible. Zero means the profile values saved when that baseline was established. For example, a brightness baseline of 30/50/70 becomes 40/60/80 at **+10**, and returns to 30/50/70 at **0**. Offsets are percentage points, never compounded. The whole group stops when any display reaches 0 or 100, preserving its differences, and the shared thumb shows the actual offset. **Reset to 0** restores all three baseline controls.

Each mode saves its own brightness, contrast, and saturation values for Day and Night, including across app restarts. Returning to a saved mode restores its values and applies the selected profile. If Auto is enabled edits remain active until the next sun transition; with Auto off the settings stay active. For example, Individual brightness values of 30/50/70 remain saved when Mass is set to 80/80/80; returning to Individual restores 30/50/70, and returning to Mass restores 80/80/80. The first visit to a mode starts with the current values. This upgrade preserves existing values and begins recording separate mode memories; it cannot recover values overwritten before the feature existed. A **Linked** indicator identifies Mass and Offset modes. Shared edits affect only the edited control in the selected profile. Unsupported controls are skipped, and disconnected profiles stay saved. Offset owns separate Day and Night baselines and signed adjustments; edits in other modes do not reset them. A change in connected displays establishes a new zero from the current Offset values without changing those values. Monitor inclusion and original hardware values remain shared across modes. The current mode supplies the saved Day and Night profiles used by automatic scheduling.

**Additional settings** starts collapsed beneath each monitor's three main sliders. In Mass and Offset it sits below the shared sliders, with a section for each display. Opening it only reveals controls; it does not apply settings.

- **Sharpness, warmth, RGB balance, and gamma** appear only when the display reports usable support. Sharpness and RGB use the monitor's native range. Warmth offers supported Kelvin values, with lower values warmer; custom RGB replaces a warmth preset and vice versa. Gamma uses reported absolute/relative values or native modes, labeled Mode 1, Mode 2, etc. when no numerical gamma value is supplied. These hardware controls save separate Day/Night values in each mode. Once edited, automatic scheduling switches them at the midpoint of the transition; the three main controls retain their smooth fade. Failed hardware writes or mismatched readbacks appear on the monitor card.
- Brightness uses its full 0–100 range in every mode, presets, and automatic scheduling. Offset stops the group when a display reaches 0 or 100 so differences are preserved. Older saved minimum/maximum brightness settings are ignored and removed on the next save.
- **My presets** saves the selected display's current Day or Night values, including extra picture controls. Enter a name and choose **Save current as preset**; using the same name updates it. **Apply** restores the saved values to the selected Day/Night profile for that display. Up to 30 presets per display are retained across restarts and all three modes.
- Optional global shortcuts **Ctrl + Alt + Shift + 1–9** apply assigned presets even from the tray. Shortcuts must be unique; a conflict with another application is reported and the Apply button remains available. Delete removes a preset and releases its shortcut.

Native control support depends on the display, picture mode and connection. The app reads capabilities without changing extra picture controls during an upgrade. Original RGB readings and the original color preset are preserved for **Restore originals**. The diagnostic preview uses simulated capabilities; it does not imply that a specific pictured monitor supports all controls.

Saturation uses the monitor's DDC control when available, or NVIDIA Digital Vibrance when the monitor does not support it. With NVIDIA, **50% is normal**, lower values reduce color, and higher values make colors richer. The first detection preserves your current driver setting. Day and night values are saved separately and follow the same scheduled fade as brightness.

Closing the window keeps the app in the system tray. Single-click the sun icon to open three vertical brightness, contrast, and saturation bars. Drag upward to increase a value. In Individual mode, click a numbered display button (using Windows display numbers, such as 1, 2, 3); the selected monitor name appears below the buttons. Switching displays loads their saved values without changing them. Arrow keys also select display buttons and adjust focused sliders. Mass and Offset use the same linked controls and saved values as the main window. Day/Night buttons apply and edit the selected profile. The **Auto** switch beside them turns sunrise/sunset scheduling on or off and stays synchronized with the main window. Turning it off keeps the selected profile active; turning it on follows the current sun schedule immediately. If a location is missing, it opens location setup. Unsupported controls are disabled. Changes save automatically and retain the normal Auto behavior. The panel follows the selected Day/Night appearance, stays inside the current display work area, and scrolls on shorter screens. Click elsewhere or press Escape to dismiss it. Double-click the sun icon or choose **Open full settings** to reopen the main window. Right-click also offers **Quick display controls**. The tray menu can pause automation for an hour, use a day/night profile for an hour, restore original monitor values, or quit. Overrides apply immediately; the 30-minute fade applies to scheduled sunrise/sunset transitions. Quitting leaves the current monitor values in place. **Restore original settings** restores the raw values recorded when each monitor was first successfully detected and pauses automatic control. Restore, Resume Auto, and explicit profile commands cancel older unsent slider writes. Completion of an older operation cannot overwrite the UI state for a newer selection.

## How it works

- Reads and writes DDC/CI brightness (`0x10`), contrast (`0x12`), and color saturation (`0x8A`) through Windows DXVA2. Color preset (`0x14`) is kept separate and restored during upgrade from older builds. Values are scaled to each monitor's reported maximum; unsupported controls are disabled independently.
- Falls back to the installed NVIDIA driver's per-display Digital Vibrance API for saturation. Displays are matched by Windows display name; the driver's minimum, default, and maximum define the slider range. Writes are read back before reporting success. Original NVIDIA values are stored separately from DDC values for restoration. NVAPI's DVC extension is private and may be unavailable on some drivers or display modes; unsupported AMD/Intel saturation is not emulated with brightness or gamma.
- Calculates solar position and horizon crossings locally using NOAA equations. No network is needed for the daily schedule after saving a location.
- Handles the location's time zone and daylight-saving transitions, midnight/date changes, and polar day/night. The calculation estimates sunrise/sunset; it does not measure room light, cloud cover or weather.
- Uses a smooth easing curve for transitions. A 10-second scheduler sends writes only when the rounded hardware target changes. Failed commands back off for one minute. Displays are rechecked every five minutes and after display or resume notifications.
- Keeps separate monitor profiles. EDID serials are used when available; monitors without usable serials are associated with their Windows display instance and may need reconfiguration after changing ports.
- Marks Windows' primary desktop monitor as the main display. Runs DDC calls on a serialized worker, keeping the interface responsive. A single-instance guard prevents duplicate schedulers.

## Weather and appearance

Current temperature and conditions come from [Open-Meteo](https://open-meteo.com/) (CC BY 4.0). Requests send latitude and longitude rounded to one decimal place, never the saved precise coordinates. Weather refreshes every 15 minutes while the panel is visible, times out after 8 seconds, and retries failures after 5 minutes. A recent in-memory value is marked cached after a failure; values older than two hours are removed. Changing location cancels previous requests. Weather failure never blocks monitor controls or the offline sun schedule.

Rain and snow add gentle particles only behind the header. They do not intercept input, pause while hidden or minimized, and respect Windows' animation preference. Settings can disable weather or its effects separately. Settings groups schedule, appearance/startup and connected displays, with a fixed close button and footer.

**Sunrise and sunset reminders** are enabled by default and can be turned off under Settings → Schedule. While SolarDesk is running, a Windows tray notification appears about 30 minutes before each local sun event. It includes the event time and the scheduled brightness-fade interval, including your timing offsets. If Auto is off, temporarily paused, or has no enabled connected displays, the message says so. Reminders use the saved city's time zone and work offline, including while the window is hidden. Windows notification preferences may hide them. Delivery is limited to the first minute of the reminder window, so waking or starting the app later does not replay stale notifications. Delivered event IDs are saved to prevent duplicates after restart. Days without a sunrise or sunset have no reminder for that event.

SolarDesk uses software rendering to avoid partially unpainted window surfaces on affected graphics-driver configurations. This changes only the control panel's rendering, not monitor hardware settings.

## Compatibility and troubleshooting

DDC/CI must be enabled in the monitor's on-screen menu. Some docks, adapters, HDR modes or monitor presets can block one or both controls. Click **Refresh displays** after enabling DDC/CI or changing a connection. Built-in laptop panel/WMI brightness control is not implemented; this app targets external DDC/CI monitors. Use only one automatic DDC controller at a time to avoid competing settings.

Automatic location requires Windows Location Services. The first-launch setup explains its purpose; clicking **Use my location** invokes the native permission API on the foreground UI thread. Coordinates are read only after access is allowed. Windows determines location using its configured providers; SolarDesk makes no IP-geolocation or reverse-geocoding web request. A timeout or denial preserves the saved location and offers retry, Windows settings, or city search. The permission decision is made by you in Windows. Windows controls whether it shows a system prompt based on its version and existing permissions.

City labels and search use a bundled GeoNames city catalog. Automatic location uses your actual saved coordinates for solar calculations; a nearby city is only a label, prefixed with "Near" beyond 20 km. Locations more than 150 km from a listed city use "Saved location". Search uses the selected city's coordinates and time zone. The catalog includes larger cities and administrative capitals, not every village or municipal boundary.

**Settings → Timing & coordinates → Coordinates** provides precise manual input and a time-zone override. Automatic location uses your Windows time zone. Location is stored until you explicitly update it; the app does not silently track travel. Sunrise/sunset calculations continue offline after setup. Windows 10 version 2004 or newer is required. City data is attributed in Settings and in the bundled `GeoNames-NOTICE.txt` (CC BY 4.0).

## Local data

Settings and recovery backup: `%LOCALAPPDATA%\SolarDesk\settings.json` and `settings.json.bak`.

Errors: `%LOCALAPPDATA%\SolarDesk\errors.log` (rotated at about 1 MB).

Startup entry, only if enabled: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\SolarDesk`.

Settings are written through a temporary file. A readable previous version is kept as a backup. If settings cannot be read or saved, automatic control stays paused. Disconnected display profiles are retained. To remove the app, first disable start at sign-in and quit, then delete the app folder; delete the local data folder separately if desired.

## Build and checks

Requires a .NET 8 SDK on Windows. No third-party NuGet libraries are used. The first standalone publish downloads Microsoft's runtime and Windows SDK targeting packages from NuGet.

```powershell
.\build.ps1                 # Standalone Windows x64 executable
.\build.ps1 -FrameworkDependent  # Smaller build; requires .NET 8 Desktop Runtime
.\test.ps1                  # Core tests only; no windows or monitor hardware access
```

If a project-local SDK is present in `.tools\dotnet`, the scripts use it; otherwise they use `dotnet` from PATH. Run UI checks in GitHub Actions: they show and focus demo windows, despite the process being launched with a hidden startup style. On a dedicated test desktop only, use `test.ps1 -IncludeUi`. Test reports and the rendered UI preview are saved under `artifacts`. These checks use simulated monitors and do not change real monitor settings.

Diagnostic modes:

```powershell
SolarDesk.exe --probe --output monitor-probe.json
SolarDesk.exe --verify-ddc --output ddc-verification.json
SolarDesk.exe --ui-smoke --output ui-smoke.txt --render preview.png
SolarDesk.exe --location-api-check --output location-api-check.txt
```

`--probe` only reads monitor values. `--verify-ddc` writes each exact current raw value back to its monitor and checks the readback; it does not apply day/night profiles. Do not run diagnostics alongside an active DDC scheduler. UI smoke/render mode uses a demo location and three monitors, does not access monitor hardware or network, and does not write your settings. Tests cover profile bindings, keyboard slider commands, vertical range direction, first-launch setup, dismissal, denied permission, city search, and Settings. Previews of the main panel, onboarding, and Settings are saved in `artifacts`. `--location-api-check` confirms API availability without requesting permission or reading location.

## Redesign rationale

The 2.0 interface puts frequent actions on the main panel and moves configuration into a separate Settings surface. One shared Day/Night selector replaces repeated per-monitor switches; one Auto toggle replaces separate start/pause/profile buttons. The main screen retains only monitor identity, live controls, a city chip, and a concise solar status. Native vector graphics stay crisp at Windows scaling levels without loading decorative bitmap assets.

- [Twinkle Tray](https://twinkletray.com/) informed the emphasis on quick access to monitor brightness.
- [Lunar's Location Mode](https://lunar.fyi/pro) informed the clear relationship between location, the sun, and automatic brightness.
- [Nielsen Norman Group: progressive disclosure](https://www.nngroup.com/articles/progressive-disclosure/) supports moving occasional settings off the primary control surface.
- [Microsoft slider guidance](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/slider) informed immediate feedback, clear value labels, keyboard support, and upward-increasing vertical controls.
- [Microsoft location guidance](https://learn.microsoft.com/en-us/windows/uwp/maps-and-location/guidelines-and-checklist-for-detecting-location) informed explaining the purpose before requesting access, foreground permission handling, and recovery after denial.
- [GeoNames data format and license](https://download.geonames.org/export/dump/readme.txt) supports offline city labeling without sending coordinates to a third-party geocoder.

## API and calculation references

- [Microsoft: active display resolution](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-enumdisplaysettingsw)
- [Open-Meteo: current weather and WMO codes](https://open-meteo.com/en/docs)
- [Microsoft: low-level monitor configuration](https://learn.microsoft.com/en-us/windows/win32/monitor/using-the-low-level-monitor-configuration-functions)
- [Microsoft: GetVCPFeatureAndVCPFeatureReply](https://learn.microsoft.com/en-us/windows/win32/api/lowlevelmonitorconfigurationapi/nf-lowlevelmonitorconfigurationapi-getvcpfeatureandvcpfeaturereply)
- [NOAA solar calculation details](https://gml.noaa.gov/grad/solcalc/calcdetails.html)
- [NOAA solar equations](https://gml.noaa.gov/grad/solcalc/solareqns.PDF)
- [Microsoft: Windows location and permission flow](https://learn.microsoft.com/en-us/windows/apps/develop/maps-and-location/get-location)
- [Microsoft: Geolocator.RequestAccessAsync](https://learn.microsoft.com/en-us/uwp/api/windows.devices.geolocation.geolocator.requestaccessasync)
- [NVIDIA: Digital Vibrance](https://www.nvidia.com/content/Control-Panel-Help/vLatest/en-us/mergedProjects/nvdsp/To_use_Digital_Vibrance_to_make_your_desktop_colors_richer.htm)
- [NVAPI DVC ABI signatures](https://github.com/falahati/NvAPIWrapper/blob/master/NvAPIWrapper/Native/Delegates/Display.cs), [structure layout](https://github.com/falahati/NvAPIWrapper/blob/master/NvAPIWrapper/Native/Display/Structures/PrivateDisplayDVCInfoEx.cs), and [interface IDs](https://github.com/falahati/NvAPIWrapper/blob/master/NvAPIWrapper/Native/Helpers/FunctionId.cs)
