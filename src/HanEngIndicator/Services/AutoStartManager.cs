using Microsoft.Win32;

namespace HanEngIndicator.Services;

/// <summary>
/// Manages "start with Windows" via the per-user (HKCU) Run key. Per-user only -
/// no administrator rights are required and nothing machine-wide is touched.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "HanEngIndicator";

    /// <summary>
    /// True only if the Run key points at THIS executable. If the app was moved
    /// to a different folder, the stale entry is treated as "not enabled" so the
    /// user is prompted to re-register at the new path.
    /// </summary>
    public static bool IsEnabled()
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: false);
            if (key?.GetValue(ValueName) is not string stored)
            {
                return false;
            }

            return string.Equals(Normalize(stored), Normalize(CurrentCommand()),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, writable: true);
            if (enabled)
            {
                key.SetValue(ValueName, CurrentCommand());
            }
            else if (key.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string CurrentCommand()
    {
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        return $"\"{exe}\"";
    }

    private static string Normalize(string command)
    {
        string s = command.Trim().Trim('"');
        try
        {
            return Path.GetFullPath(s);
        }
        catch
        {
            return s;
        }
    }
}
