# Changelog

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
