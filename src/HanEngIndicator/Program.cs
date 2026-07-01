using HanEngIndicator.Forms;
using HanEngIndicator.Services;

namespace HanEngIndicator;

internal static class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    private static void Main()
    {
        // Single instance: if another copy is already running, exit quietly.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, "HanEngIndicator_SingleInstance_8b3f", out bool isNew);
        if (!isNew)
        {
            return;
        }

        // We do not use the WinForms source generator (ApplicationConfiguration)
        // so the project can also be cross-built on non-Windows CI. Configure
        // the equivalent settings manually.
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        Application.ThreadException += (_, e) => SafeLog("UI thread exception: " + e.Exception.GetType().Name);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            SafeLog("Unhandled exception: " + (e.ExceptionObject as Exception)?.GetType().Name);

        try
        {
            Application.Run(new TrayApplicationContext());
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }

    private static void SafeLog(string message)
    {
        try
        {
            // Dispose flushes the buffered write synchronously, which matters on
            // the crash path where the process may be about to terminate.
            using var logger = new DiagnosticLogger { Enabled = true };
            logger.Log(message);
        }
        catch
        {
            // last-resort: swallow
        }
    }
}
