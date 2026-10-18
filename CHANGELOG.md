# Changelog

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
