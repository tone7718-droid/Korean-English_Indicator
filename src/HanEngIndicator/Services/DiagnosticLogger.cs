using System.Globalization;
using System.Text;
using HanEngIndicator.Settings;

namespace HanEngIndicator.Services;

/// <summary>
/// Optional, opt-in diagnostic logger for compatibility troubleshooting
/// (e.g. with a hospital chart program that uses a custom input control).
///
/// PRIVACY GUARANTEE: This logger only ever records non-sensitive technical
/// metadata - window class names, thread ids, keyboard-layout ids, IME
/// open/conversion status, and whether caret detection succeeded and via which
/// method. It NEVER records typed characters, clipboard data, or patient
/// information. There is no networking of any kind.
/// </summary>
public sealed class DiagnosticLogger
{
    private readonly object _gate = new();
    private const long MaxBytes = 1_000_000; // ~1 MB cap; oldest is rotated out.

    public bool Enabled { get; set; }

    public static string LogFilePath =>
        Path.Combine(AppSettings.SettingsDirectory, "diagnostics.log");

    public void Log(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (_gate)
            {
                Directory.CreateDirectory(AppSettings.SettingsDirectory);
                RotateIfNeeded();

                string line = string.Create(CultureInfo.InvariantCulture,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}{Environment.NewLine}");
                File.AppendAllText(LogFilePath, line, Encoding.UTF8);
            }
        }
        catch
        {
            // Logging must never disrupt the app.
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(LogFilePath);
            if (fi.Exists && fi.Length > MaxBytes)
            {
                string backup = LogFilePath + ".old";
                File.Delete(backup);
                File.Move(LogFilePath, backup);
            }
        }
        catch
        {
            // ignore rotation failures
        }
    }
}
