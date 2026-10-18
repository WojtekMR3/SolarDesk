# Privacy

Sunset Monitor does not have accounts, analytics, advertising, crash uploads, or automatic update checks.

## Stored on your computer

`%LOCALAPPDATA%\SolarDesk` contains settings, a recovery backup, and local error logs. The folder retains the app's former name so upgrades preserve existing data. Settings include the selected location and time zone, monitor identifiers, original monitor values, profiles, presets, and preferences. Disconnected monitor profiles remain saved. Logs can contain device details and local paths; review them before sharing.

Enabling **Start with Windows** adds an entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\Sunset Monitor`. On first launch after upgrading, an enabled legacy `SolarDesk` entry moves to the new executable and name. Disabled startup stays disabled. Disabling the option removes both current and legacy entries.

## Location and weather

Choosing a city uses a bundled offline GeoNames catalog. **Use my location** asks Windows for location access. Windows controls permission and the location providers; Sunset Monitor does not silently track movement or use a web geocoder. Solar calculations run locally using the saved coordinates.

Weather is enabled by default once a location is set. While the main window is visible and active, the app requests current weather from `api.open-meteo.com`, normally every 15 minutes. Requests pause while another app has focus, the window is minimized, or Sunset Monitor is in the tray. It sends coordinates rounded to one decimal place. Like other web requests, this exposes your IP address to that service. See [Open-Meteo's terms and privacy policy](https://open-meteo.com/en/terms). Disable weather in Settings to stop those requests; monitor controls and sun scheduling continue working.

## Removing local data

Disable **Start with Windows**, quit Sunset Monitor from the tray menu, and delete its application folder. Delete `%LOCALAPPDATA%\SolarDesk` to erase saved settings and logs. Monitor values stay at their last setting unless you restore them before quitting.
