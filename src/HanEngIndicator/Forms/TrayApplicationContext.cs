using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using HanEngIndicator.Models;
using HanEngIndicator.Native;
using HanEngIndicator.Services;
using HanEngIndicator.Settings;

namespace HanEngIndicator.Forms;

/// <summary>
/// The application root: owns the tray icon, the settings menu, the background
/// polling worker and the overlay. There is no main window - the app lives in
/// the tray.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly DiagnosticLogger _logger;
    private readonly ImeDetector _detector;
    private readonly CaretLocator _caret;
    private readonly OverlayForm _overlay;
    private readonly NotifyIcon _tray;
    private Icon? _trayIconHandle;

    // Background polling worker + coordination.
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _wake = new(false);
    private volatile int _intervalMs;

    public TrayApplicationContext()
    {
        _settings = AppSettings.Load();
        _logger = new DiagnosticLogger { Enabled = _settings.DiagnosticLogging };
        _detector = new ImeDetector(_logger);
        _caret = new CaretLocator(_logger);
        _overlay = new OverlayForm();

        _tray = new NotifyIcon
        {
            Text = "HanEng Indicator (한/영 표시기)",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        SetTrayIcon(BuildTrayIcon(InputMode.Korean, "가"));
        _lastTrayGlyph = "가";
        _tray.DoubleClick += (_, _) => ToggleEnabled();

        // Ensure the overlay window handle exists on the UI thread so the worker
        // can marshal updates to it via BeginInvoke from the very first cycle.
        _ = _overlay.Handle;

        // All the heavy, potentially-blocking inter-process inspection (IME
        // queries via SendMessageTimeout, and UI Automation) runs on this
        // dedicated background thread - NOT on the UI thread. The UI thread only
        // paints the badge and services the tray menu, so it can never be hung
        // by a slow/unresponsive foreground app. The thread stays MTA (default),
        // which is also the correct/safe apartment for UI Automation clients.
        _intervalMs = _settings.PollingIntervalMs;
        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "HanEngIndicator.Worker",
        };
        _worker.Start();

        _logger.Log("---- HanEngIndicator started ----");
    }

    // ----- main loop (background worker thread) -----------------------------

    private bool _suppressAutoStartEvent;              // UI thread only
    private string _lastTrayGlyph = string.Empty;      // UI thread only
    private InputMode _lastGoodMode = InputMode.Unknown; // worker thread only
    private bool _lastGoodCaps;                          // worker thread only
    private DateTime _lastGoodAtUtc = DateTime.MinValue; // worker thread only

    /// <summary>A single unit of work handed from the worker to the UI thread.</summary>
    private readonly record struct OverlayCommand(
        bool Show, InputMode Mode, bool CapsLock, Point Location, int BadgeSize, double Opacity)
    {
        public static OverlayCommand Idle { get; } =
            new(false, InputMode.Unknown, false, Point.Empty, 0, 1.0);
    }

    private void WorkerLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            try
            {
                RunOneCycle();
            }
            catch (Exception ex)
            {
                _logger.Log("worker " + ex.GetType().Name);
            }

            // Sleep until the next cycle, waking early if settings changed or we
            // are shutting down.
            if (_wake.Wait(Math.Max(30, _intervalMs)))
            {
                _wake.Reset();
            }
        }
    }

    private void RunOneCycle()
    {
        if (!_settings.Enabled)
        {
            PostToUi(OverlayCommand.Idle);
            return;
        }

        InputStateSnapshot snapshot = _detector.Detect();

        if (snapshot.Mode != InputMode.Unknown)
        {
            _lastGoodMode = snapshot.Mode;
            _lastGoodCaps = snapshot.CapsLock;
            _lastGoodAtUtc = DateTime.UtcNow;
        }

        int graceMs = Math.Max(800, _intervalMs * 6);
        OverlayDecision decision = OverlayPolicy.Decide(
            snapshot, _lastGoodMode, _lastGoodCaps, _lastGoodAtUtc,
            DateTime.UtcNow, graceMs, _settings.DisplayPolicy);

        if (!decision.Show)
        {
            // Still carry the mode/caps so the tray icon stays accurate even
            // while the overlay is hidden (e.g. English under Korean-only).
            PostToUi(new OverlayCommand(false, decision.Mode, decision.CapsLock, Point.Empty, 0, _settings.Opacity));
            return;
        }

        PostToUi(ComputePlacement(snapshot, decision));
    }

    /// <summary>
    /// Compute the badge placement. Position is derived from the CURRENT
    /// foreground/caret (via <paramref name="snapshot"/>), never from a stale
    /// grace-period state; only the glyph (mode/caps) comes from the decision.
    /// Runs on the worker thread.
    /// </summary>
    private OverlayCommand ComputePlacement(InputStateSnapshot snapshot, OverlayDecision decision)
    {
        CaretAnchor anchor = _caret.Locate(snapshot, _settings);

        Point reference = anchor.HasValue
            ? new Point(anchor.ScreenRect.Right, anchor.ScreenRect.Bottom)
            : Control.MousePosition;

        uint dpi = GetForegroundDpi(reference);
        Rectangle workArea = WorkingAreaForPoint(reference);

        int badge = PositionCalculator.BadgeSizeForDpi(dpi, _settings.FontScale);
        Point offsetPx = PositionCalculator.ScaleOffset(new Point(_settings.OffsetX, _settings.OffsetY), dpi);
        var badgeSize = new Size(badge, badge);

        Point topLeft;
        if (_settings.PositionMode == PositionMode.FixedCorner || !anchor.HasValue)
        {
            Point margin = PositionCalculator.ScaleOffset(new Point(8, 8), dpi);
            topLeft = PositionCalculator.ComputeCorner(_settings.FixedCorner, badgeSize, margin, workArea);
        }
        else
        {
            topLeft = PositionCalculator.ComputeTopLeft(anchor.ScreenRect, badgeSize, offsetPx, workArea);
        }

        return new OverlayCommand(true, decision.Mode, decision.CapsLock, topLeft, badge, _settings.Opacity);
    }

    /// <summary>Marshal a command to the UI thread. Never blocks the worker.</summary>
    private void PostToUi(OverlayCommand cmd)
    {
        try
        {
            if (_overlay.IsHandleCreated)
            {
                _overlay.BeginInvoke((Action)(() => ApplyOnUi(cmd)));
            }
        }
        catch (Exception ex)
        {
            // Handle may be tearing down during shutdown; ignore.
            _logger.Log("post " + ex.GetType().Name);
        }
    }

    /// <summary>Apply a command. UI thread only - lightweight, never blocks.</summary>
    private void ApplyOnUi(OverlayCommand cmd)
    {
        try
        {
            UpdateTrayIcon(cmd.Mode, cmd.CapsLock);

            if (cmd.Show)
            {
                _overlay.ShowBadge(cmd.Mode, cmd.CapsLock, cmd.Location, cmd.BadgeSize, cmd.Opacity);
            }
            else
            {
                _overlay.HideBadge();
            }
        }
        catch (Exception ex)
        {
            _logger.Log("apply " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// DPI for the target monitor. Prefer GetDpiForWindow(foreground) - the
    /// recommended call for a Per-Monitor-v2 process - and fall back to the
    /// monitor under the reference point.
    /// </summary>
    private static uint GetForegroundDpi(Point reference)
    {
        try
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg != IntPtr.Zero)
            {
                uint d = NativeMethods.GetDpiForWindow(fg);
                if (d > 0)
                {
                    return d;
                }
            }
        }
        catch
        {
            // GetDpiForWindow needs Win10 1607+; fall through.
        }

        return GetDpiForPoint(reference);
    }

    private static uint GetDpiForPoint(Point p)
    {
        try
        {
            IntPtr mon = NativeMethods.MonitorFromPoint(
                new NativeMethods.POINT { X = p.X, Y = p.Y },
                NativeMethods.MONITOR_DEFAULTTONEAREST);
            if (NativeMethods.GetDpiForMonitor(mon, NativeMethods.MonitorDpiType.Effective, out uint dpiX, out _) == 0
                && dpiX > 0)
            {
                return dpiX;
            }
        }
        catch
        {
            // shcore may be unavailable on very old systems; fall back to 96.
        }

        return 96;
    }

    private static Rectangle WorkingAreaForPoint(Point p)
    {
        try
        {
            return Screen.FromPoint(p).WorkingArea;
        }
        catch
        {
            return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
        }
    }

    // ----- tray icon --------------------------------------------------------

    private void UpdateTrayIcon(InputMode mode, bool capsLock)
    {
        if (mode == InputMode.Unknown)
        {
            return;
        }

        string glyph = BadgeText.Glyph(mode, capsLock);
        if (glyph.Length == 0 || glyph == _lastTrayGlyph)
        {
            return;
        }

        _lastTrayGlyph = glyph;
        SetTrayIcon(BuildTrayIcon(mode, glyph));
    }

    /// <summary>
    /// Swap the tray icon and fully release the previous one. Icon.FromHandle
    /// does NOT own the HICON produced by Bitmap.GetHicon(), so we must destroy
    /// it explicitly - otherwise every glyph change leaks a GDI handle and, over
    /// a long session, rendering can eventually fail.
    /// </summary>
    private void SetTrayIcon(Icon newIcon)
    {
        Icon? old = _trayIconHandle;
        _tray.Icon = newIcon;
        _trayIconHandle = newIcon;

        if (old is not null)
        {
            IntPtr oldHandle = old.Handle;
            old.Dispose();
            NativeMethods.DestroyIcon(oldHandle);
        }
    }

    private static Icon BuildTrayIcon(InputMode mode, string glyph)
    {
        bool korean = mode == InputMode.Korean;
        Color back = korean
            ? Color.FromArgb(0x1F, 0x6F, 0xD6)
            : Color.FromArgb(0xD9, 0x6A, 0x00);

        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(back);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font(korean ? "Malgun Gothic" : "Segoe UI",
                korean ? 16f : 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tb = new SolidBrush(Color.White);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(glyph, font, tb, new RectangleF(0, 0, 32, 32), fmt);
        }

        return Icon.FromHandle(bmp.GetHicon());
    }

    // ----- menu -------------------------------------------------------------

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        var enabled = new ToolStripMenuItem("표시 활성화 (Enabled)") { CheckOnClick = true, Checked = _settings.Enabled };
        enabled.CheckedChanged += (_, _) => { _settings.Enabled = enabled.Checked; Persist(); };

        // Display policy
        var both = new ToolStripMenuItem("한글·영문 모두 표시") { Checked = _settings.DisplayPolicy == DisplayPolicy.Always };
        var koreanOnly = new ToolStripMenuItem("한글일 때만 표시") { Checked = _settings.DisplayPolicy == DisplayPolicy.KoreanOnly };
        both.Click += (_, _) => SetDisplayPolicy(DisplayPolicy.Always, both, koreanOnly);
        koreanOnly.Click += (_, _) => SetDisplayPolicy(DisplayPolicy.KoreanOnly, both, koreanOnly);

        // Position
        var caretPos = new ToolStripMenuItem("캐럿 옆 표시") { Checked = _settings.PositionMode == PositionMode.CaretThenMouse };
        var mousePos = new ToolStripMenuItem("마우스 옆 표시") { Checked = _settings.PositionMode == PositionMode.Mouse };
        var fixedPos = new ToolStripMenuItem("화면 고정 위치 표시") { Checked = _settings.PositionMode == PositionMode.FixedCorner };
        caretPos.Click += (_, _) => SetPositionMode(PositionMode.CaretThenMouse, caretPos, mousePos, fixedPos);
        mousePos.Click += (_, _) => SetPositionMode(PositionMode.Mouse, caretPos, mousePos, fixedPos);
        fixedPos.DropDownItems.AddRange(BuildCornerItems());
        fixedPos.Click += (_, _) => SetPositionMode(PositionMode.FixedCorner, caretPos, mousePos, fixedPos);

        // Size / opacity / offset
        var sizeMenu = new ToolStripMenuItem("글자 크기");
        sizeMenu.DropDownItems.AddRange(BuildScaleItems());
        var opacityMenu = new ToolStripMenuItem("투명도");
        opacityMenu.DropDownItems.AddRange(BuildOpacityItems());
        var offsetMenu = new ToolStripMenuItem("오프셋(거리)");
        offsetMenu.DropDownItems.AddRange(BuildOffsetItems());

        var autoStart = new ToolStripMenuItem("Windows 시작 시 자동 실행") { CheckOnClick = true, Checked = AutoStartManager.IsEnabled() };
        autoStart.CheckedChanged += (_, _) =>
        {
            if (_suppressAutoStartEvent)
            {
                return;
            }

            bool ok = AutoStartManager.SetEnabled(autoStart.Checked);
            if (!ok)
            {
                // Registry write failed (permissions / security software). Revert
                // the checkbox so it never shows an untrue state, and tell the user.
                _suppressAutoStartEvent = true;
                autoStart.Checked = !autoStart.Checked;
                _suppressAutoStartEvent = false;
                _tray.ShowBalloonTip(4000, "HanEng Indicator",
                    "자동 실행 설정을 변경하지 못했습니다. (권한 또는 보안 프로그램 차단)",
                    ToolTipIcon.Warning);
                return;
            }

            _settings.AutoStart = autoStart.Checked;
            Persist();
        };

        var diag = new ToolStripMenuItem("진단 로그 기록") { CheckOnClick = true, Checked = _settings.DiagnosticLogging };
        diag.CheckedChanged += (_, _) =>
        {
            _settings.DiagnosticLogging = diag.Checked;
            _logger.Enabled = diag.Checked;
            Persist();
            _logger.Log("Diagnostic logging enabled.");
        };

        var openFolder = new ToolStripMenuItem("설정 폴더 열기");
        openFolder.Click += (_, _) => OpenSettingsFolder();

        var exit = new ToolStripMenuItem("종료 (Exit)");
        exit.Click += (_, _) => ExitApp();

        menu.Items.AddRange(new ToolStripItem[]
        {
            enabled,
            new ToolStripSeparator(),
            both, koreanOnly,
            new ToolStripSeparator(),
            caretPos, mousePos, fixedPos,
            new ToolStripSeparator(),
            sizeMenu, opacityMenu, offsetMenu,
            new ToolStripSeparator(),
            autoStart, diag, openFolder,
            new ToolStripSeparator(),
            exit,
        });

        return menu;
    }

    private ToolStripItem[] BuildCornerItems()
    {
        (string label, ScreenCorner corner)[] corners =
        {
            ("우측 하단", ScreenCorner.BottomRight),
            ("좌측 하단", ScreenCorner.BottomLeft),
            ("우측 상단", ScreenCorner.TopRight),
            ("좌측 상단", ScreenCorner.TopLeft),
        };

        var items = new List<ToolStripItem>();
        foreach ((string label, ScreenCorner corner) in corners)
        {
            var item = new ToolStripMenuItem(label) { Checked = _settings.FixedCorner == corner };
            item.Click += (_, _) => { _settings.FixedCorner = corner; _settings.PositionMode = PositionMode.FixedCorner; Persist(); };
            items.Add(item);
        }

        return items.ToArray();
    }

    private ToolStripItem[] BuildScaleItems()
    {
        (string label, double scale)[] options =
        {
            ("작게 (0.85x)", 0.85), ("보통 (1.0x)", 1.0), ("크게 (1.25x)", 1.25), ("아주 크게 (1.6x)", 1.6),
        };
        return options.Select(o =>
        {
            var item = new ToolStripMenuItem(o.label) { Checked = Math.Abs(_settings.FontScale - o.scale) < 0.001 };
            item.Click += (_, _) => { _settings.FontScale = o.scale; Persist(); };
            return (ToolStripItem)item;
        }).ToArray();
    }

    private ToolStripItem[] BuildOpacityItems()
    {
        (string label, double value)[] options =
        {
            ("50%", 0.5), ("75%", 0.75), ("100%", 1.0),
        };
        return options.Select(o =>
        {
            var item = new ToolStripMenuItem(o.label) { Checked = Math.Abs(_settings.Opacity - o.value) < 0.001 };
            item.Click += (_, _) => { _settings.Opacity = o.value; Persist(); };
            return (ToolStripItem)item;
        }).ToArray();
    }

    private ToolStripItem[] BuildOffsetItems()
    {
        (string label, int x, int y)[] options =
        {
            ("가깝게 (8,6)", 8, 6), ("보통 (12,10)", 12, 10), ("멀게 (20,16)", 20, 16),
        };
        return options.Select(o =>
        {
            var item = new ToolStripMenuItem(o.label) { Checked = _settings.OffsetX == o.x && _settings.OffsetY == o.y };
            item.Click += (_, _) => { _settings.OffsetX = o.x; _settings.OffsetY = o.y; Persist(); };
            return (ToolStripItem)item;
        }).ToArray();
    }

    // ----- menu actions -----------------------------------------------------

    private void ToggleEnabled()
    {
        _settings.Enabled = !_settings.Enabled;
        Persist();
    }

    private void SetDisplayPolicy(DisplayPolicy policy, ToolStripMenuItem both, ToolStripMenuItem koreanOnly)
    {
        _settings.DisplayPolicy = policy;
        both.Checked = policy == DisplayPolicy.Always;
        koreanOnly.Checked = policy == DisplayPolicy.KoreanOnly;
        Persist();
    }

    private void SetPositionMode(PositionMode mode, params ToolStripMenuItem[] all)
    {
        _settings.PositionMode = mode;
        foreach (ToolStripMenuItem item in all)
        {
            item.Checked = false;
        }

        Persist();
    }

    private static void OpenSettingsFolder()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.SettingsDirectory);
            Process.Start(new ProcessStartInfo
            {
                FileName = AppSettings.SettingsDirectory,
                UseShellExecute = true,
            });
        }
        catch
        {
            // ignore
        }
    }

    private void Persist()
    {
        _settings.Clamped();
        _intervalMs = _settings.PollingIntervalMs;
        _wake.Set(); // apply the new interval promptly
        _settings.Save();
        // Refresh the menu so checkmarks reflect the new state next open.
        _tray.ContextMenuStrip = BuildMenu();
    }

    private void StopWorker()
    {
        // Safe to call more than once (ExitApp then Dispose).
        try { _cts.Cancel(); } catch { /* already disposed */ }
        try { _wake.Set(); } catch { /* already disposed */ }
        // The worker may be mid-way through a bounded IME/UIA call; give it a
        // moment to unwind. It is a background thread, so even if this times out
        // the process can still exit cleanly.
        try { _worker.Join(2000); } catch { /* ignore */ }
    }

    private void ExitApp()
    {
        _logger.Log("---- HanEngIndicator exiting ----");
        StopWorker();
        _tray.Visible = false;
        _overlay.HideBadge();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            StopWorker();
            _cts.Dispose();
            _wake.Dispose();
            _tray.Dispose();
            if (_trayIconHandle is not null)
            {
                IntPtr h = _trayIconHandle.Handle;
                _trayIconHandle.Dispose();
                NativeMethods.DestroyIcon(h);
            }
            _overlay.Dispose();
            _logger.Dispose();
        }

        base.Dispose(disposing);
    }
}
