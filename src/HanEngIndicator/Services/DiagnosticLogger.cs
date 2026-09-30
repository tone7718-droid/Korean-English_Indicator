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
    private int _queued;
    private int _disposed;
    private const int MaxQueuedLines = 512;

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
        if (!_enabled || Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        if (Interlocked.Increment(ref _queued) > MaxQueuedLines)
        {
            Interlocked.Decrement(ref _queued);
            return; // Disk trouble must never allow unbounded memory growth.
        }
        try
        {
            // Enqueue is cheap and non-blocking; the writer thread does the I/O.
            _queue.Enqueue(string.Create(CultureInfo.InvariantCulture,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  {message}"));
            _signal.Set();
        }
        catch (ObjectDisposedException)
        {
            // A late log from the worker during shutdown; safe to drop.
        }
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
                Interlocked.Decrement(ref _queued);
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _enabled = false;
        _cts.Cancel();
        _signal.Set();
        // Only the writer flushes. If a disk call is stuck, leave its handles
        // alive until process exit rather than racing/disposal from this thread.
        if (_writer.Join(1500))
        {
            _signal.Dispose();
            _cts.Dispose();
        }
    }
}
