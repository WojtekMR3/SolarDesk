using Microsoft.Win32;

namespace SolarDesk;

internal static class StartupRegistration
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string LegacyName = "SolarDesk";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath);
        return key is not null && IsEnabled(key);
    }

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunPath, true);
        SetEnabled(key, enabled, Environment.ProcessPath!);
    }

    public static void MigrateLegacy()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunPath, writable: true);
        if (key is not null) MigrateLegacy(key, Environment.ProcessPath!);
    }

    internal static bool IsEnabled(RegistryKey key)
        => key.GetValue(App.DisplayName) is string || key.GetValue(LegacyName) is string;

    internal static void SetEnabled(RegistryKey key, bool enabled, string executable)
    {
        // Write first: if registration fails, retain the working legacy startup entry.
        if (enabled) key.SetValue(App.DisplayName, $"\"{executable}\" --tray");
        else key.DeleteValue(App.DisplayName, false);
        key.DeleteValue(LegacyName, false);
    }

    internal static void MigrateLegacy(RegistryKey key, string executable)
    {
        if (key.GetValue(LegacyName) is string) SetEnabled(key, true, executable);
    }
}
