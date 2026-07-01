using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using HanEngIndicator.Settings;

namespace HanEngIndicator.Services;

/// <summary>
/// Optional, opt-in diagnostic logger for compatibility troubleshooting
/// (e.g. with a hospital chart program that uses a custom input control).
///
/// Writes are BUFFERED and flushed on a dedicated background thread, so callers
/// (the UI thread and the polling worker) never block on file I/O even when
/// logging is enabled at a high polling rate.
///
/// PRIVACY GUARANTEE: This logger only ever records non-sensitive technical
/// metadata - window class names, thread ids, keyboard-layout ids, IME
/// open/conversion status, and whether caret detection succeeded and via which
/// method. It NEVER records typed characters, clipboard data, or patient
/// information. There is no networking of any kind.
/// </summary>
public sealed class DiagnosticLogger : IDisposable
{
    private readonly ConcurrentQueue<string> _queue = new();
    private readonly AutoResetEvent _signal = new(false);
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _writer;
    private volatile bool _enabled;

    private const long MaxBytes = 1_000_000; // ~1 MB cap; oldest is rotated out.

    public bool Enabled
    {
        get => _enabled;
        set => _enabled = value;
    }

    public static string LogFilePath =>
        Path.Combine(AppSettings.SettingsDirectory, "diagnostics.log");

    public DiagnosticLogger()
    {
        _writer = new Thread(WriterLoop)
        {
            IsBackground = true,
            Name = "HanEngIndicator.Log",
        };
        _writer.Start();
    }

    public void Log(string message)
    {
        if (!_enabled)
        {
            return;
        }

        // Enqueue is cheap and non-blocking; the writer thread does the I/O.
        _queue.Enqueue(string.Create(CultureInfo.InvariantCulture,
            $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}"));
        _signal.Set();
    }

    private void WriterLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            _signal.WaitOne(1000);
            Flush();
        }

        Flush(); // final drain on shutdown
    }

    private void Flush()
    {
        if (_queue.IsEmpty)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            RotateIfNeeded();

            var sb = new StringBuilder();
            while (_queue.TryDequeue(out string? line))
            {
                sb.AppendLine(line);
            }

            if (sb.Length > 0)
            {
                File.AppendAllText(LogFilePath, sb.ToString(), Encoding.UTF8);
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

    public void Dispose()
    {
        try
        {
            _cts.Cancel();
            _signal.Set();
            _writer.Join(1500);
        }
        catch
        {
            // ignore
        }

        Flush();
        _signal.Dispose();
        _cts.Dispose();
    }
}
