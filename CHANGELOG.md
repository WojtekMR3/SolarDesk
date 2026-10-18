# Changelog

## 2.9.5

- Show numbered monitor silhouettes in the tray selector, scaled to each display's current aspect ratio, including ultrawide and portrait layouts.
- Add aspect-ratio labels and a persistent MAIN marker on Windows' primary display, independently of which monitor is selected.
- Include resolution, aspect ratio, and main-display status in monitor tooltips and accessibility labels.
- Keep the vertical bars and compact tray layout without adding background polling.

## 2.9.4

- Arrange tray brightness, contrast, and saturation as three vertical bars matching the main window; dragging upward increases the value.
- Replace the display dropdown with one-click numbered buttons in Windows display-number order and show the selected monitor's name below them.
- Preserve each display's saved values when switching, keyboard access, monitor reconnect handling, and the existing Auto toggle.
- Use centered vertical controls for Offset mode and show saturation intensity from the resulting monitor values.

## 2.9.3

- Add an Auto toggle beside Day/Night in tray quick controls, synchronized with the main window and saved automation setting.
- Reuse the existing scheduling behavior: enabling Auto follows the sun immediately; disabling it keeps the selected profile active.
- Open location setup when Auto needs a location, and explain when no enabled display can accept automatic control.
- Update the toggle through existing UI events without adding background polling or timers.

## 2.9.2

- Refresh tray values whenever the panel opens, and calculate Mass/Offset controls directly from monitor profiles instead of hidden main-window controls.
- Keep tray sliders usable when the full settings overlay disables the main window's own controls.
- Show detection progress during startup, retry missing or unreadable displays when opening the tray, and add a Refresh displays action inside the panel.
- Throttle automatic tray retries and keep idle background operation quiet.
- Clarify that tray startup is optional: enabling Start with Windows starts quietly at sign-in, while normal launches also support tray controls and scheduling.
- Local test scripts now default to non-interactive core checks. UI testing and release packaging run in GitHub Actions unless explicitly requested on a dedicated test desktop.

## 2.9.1

- Stop stars, sun rays, weather particles, and theme transitions when the main window loses focus, is minimized, or closes to the tray.
- Defer weather requests until the main window is in use and cancel unfinished requests when it goes into the background.
- Start directly in the tray without first rendering the full window. Preset hotkeys remain available.
- Reduce idle background scheduling from six checks per minute to one. Fades retain ten-second updates, with earlier wake-ups for transition boundaries, overrides, and reminders.
- Defer periodic monitor rescans until the full window is in use. Display changes, wake from sleep, and explicit refresh still trigger discovery.
- Avoid redundant display notifications and rebinding a hidden quick-controls panel.
- Reduce runtime memory by keeping executable assemblies uncompressed and applying compression to the download ZIP instead.
- Add a repeatable CPU and memory benchmark using simulated monitors, without changing saved settings or real displays.

## 2.9.0

- Single-click the tray icon to open quick brightness, contrast, and saturation controls.
- Choose a display, switch Day/Night profiles, or adjust all displays using Mass and Offset modes without opening the main window.
- Quick controls share the main window's saved values, capability checks, and automatic scheduling behavior.
- The panel follows the selected profile's appearance, fits the current display's work area, and closes on Escape or when you click elsewhere.
- Double-click still opens the main window; right-click keeps the existing tray menu and adds Quick display controls.

## 2.8.3

First public release.

- Day and Night profiles with sunrise/sunset scheduling and startup/wake catch-up.
- Individual, Mass, and Offset controls with separate saved values for each profile and mode.
- Brightness, contrast, saturation, and additional picture settings where hardware supports them.
- Warm Day appearance and dark Night sky, linked to the selected profile.
- Offline city lookup, optional weather effects, sun reminders, presets, and hotkeys.
- Portable Windows x64 package with the runtime included.

Known limitations: unsigned executable; external DDC/CI monitor support varies by connection and hardware; built-in laptop brightness is not implemented; NVIDIA saturation fallback depends on a private driver interface. Automated UI checks use simulated monitors, not a comprehensive hardware compatibility matrix.
