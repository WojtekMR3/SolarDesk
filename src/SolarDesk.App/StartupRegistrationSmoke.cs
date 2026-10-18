using Microsoft.Win32;

namespace SolarDesk;

public partial class MainWindow
{
    private static void CheckStartupRegistration(Action<bool, string> check)
    {
        // Exercise migration in an isolated key; never edit the test desktop's startup entries.
        string path = @"Software\SunsetMonitor.Tests-" + Guid.NewGuid().ToString("N");
        const string executable = @"C:\Apps\Sunset Monitor\SunsetMonitor.exe";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(path, true);
            StartupRegistration.MigrateLegacy(key, executable);
            check(!StartupRegistration.IsEnabled(key) && key.ValueCount == 0,
                "Renaming preserves disabled startup without creating a registration");

            key.SetValue(StartupRegistration.LegacyName, @"C:\Old\SolarDesk.exe --tray");
            StartupRegistration.MigrateLegacy(key, executable);
            check(StartupRegistration.IsEnabled(key) && key.GetValue(StartupRegistration.LegacyName) is null
                && key.GetValue(App.DisplayName) as string == $"\"{executable}\" --tray" && key.ValueCount == 1,
                "Legacy enabled startup migrates to one correctly quoted Sunset Monitor tray command");

            StartupRegistration.MigrateLegacy(key, @"C:\Other\SunsetMonitor.exe");
            check(key.GetValue(App.DisplayName) as string == $"\"{executable}\" --tray" && key.ValueCount == 1,
                "Repeated rename migration preserves an existing current startup registration");

            key.SetValue(StartupRegistration.LegacyName, @"C:\Old\SolarDesk.exe --tray");
            StartupRegistration.SetEnabled(key, false, executable);
            check(!StartupRegistration.IsEnabled(key) && key.ValueCount == 0,
                "Disabling startup removes both current and legacy entries");
        }
        finally { Registry.CurrentUser.DeleteSubKey(path, throwOnMissingSubKey: false); }
    }
}
