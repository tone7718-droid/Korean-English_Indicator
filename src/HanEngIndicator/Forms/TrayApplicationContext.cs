using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using HanEngIndicator.Models;
using HanEngIndicator.Native;
using HanEngIndicator.Services;
using HanEngIndicator.Settings;
using Timer = System.Windows.Forms.Timer;

namespace HanEngIndicator.Forms;

/// <summary>
/// The application root: owns the tray icon, the settings menu, the polling
/// timer and the overlay. There is no main window - the app lives in the tray.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettings _settings;
    private readonly DiagnosticLogger _logger;
    private readonly ImeDetector _detector;
    private readonly CaretLocator _caret;
    private readonly OverlayForm _overlay;
    private readonly NotifyIcon _tray;
    private readonly Timer _timer;
    private Icon? _trayIconHandle;

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
            Icon = BuildTrayIcon(InputMode.Korean),
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => ToggleEnabled();

        _timer = new Timer { Interval = _settings.PollingIntervalMs };
        _timer.Tick += OnTick;
        _timer.Start();

        _logger.Log("---- HanEngIndicator started ----");
    }

    // ----- main loop --------------------------------------------------------

    private InputMode _lastTrayMode = InputMode.Unknown;

    private void OnTick(object? sender, EventArgs e)
    {
        try
        {
            if (!_settings.Enabled)
            {
                _overlay.HideBadge();
                return;
            }

            InputStateSnapshot snapshot = _detector.Detect();
            UpdateTrayIcon(snapshot.Mode);

            bool show = snapshot.Mode switch
            {
                InputMode.Unknown => false,
                InputMode.English => _settings.DisplayPolicy == DisplayPolicy.Always,
                InputMode.Korean => true,
                _ => false,
            };

            if (!show)
            {
                _overlay.HideBadge();
                return;
            }

            PlaceOverlay(snapshot);
        }
        catch (Exception ex)
        {
            _logger.Log("ERROR tick: " + ex.GetType().Name);
        }
    }

    private void PlaceOverlay(InputStateSnapshot snapshot)
    {
        CaretAnchor anchor = _caret.Locate(snapshot, _settings);

        // Reference point used to pick the monitor + DPI.
        Point reference = anchor.HasValue
            ? new Point(anchor.ScreenRect.Right, anchor.ScreenRect.Bottom)
            : Cursor.Position;

        uint dpi = GetDpiForPoint(reference);
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

        _overlay.ShowBadge(snapshot.Mode, topLeft, badge, _settings.Opacity);
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

    private void UpdateTrayIcon(InputMode mode)
    {
        if (mode == InputMode.Unknown || mode == _lastTrayMode)
        {
            return;
        }

        _lastTrayMode = mode;
        UpdateTrayIconImage(mode);
    }

    private void UpdateTrayIconImage(InputMode mode)
    {
        Icon newIcon = BuildTrayIcon(mode);
        _tray.Icon = newIcon;
        _trayIconHandle?.Dispose();
        _trayIconHandle = newIcon;
    }

    private static Icon BuildTrayIcon(InputMode mode)
    {
        Color back = mode == InputMode.Korean
            ? Color.FromArgb(0x1F, 0x6F, 0xD6)
            : Color.FromArgb(0xD9, 0x6A, 0x00);
        string text = mode == InputMode.Korean ? "가" : "A";

        using var bmp = new Bitmap(32, 32);
        using (Graphics g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(back);
            g.FillEllipse(brush, 1, 1, 30, 30);
            using var font = new Font(mode == InputMode.Korean ? "Malgun Gothic" : "Segoe UI",
                mode == InputMode.Korean ? 16f : 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var tb = new SolidBrush(Color.White);
            using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString(text, font, tb, new RectangleF(0, 0, 32, 32), fmt);
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
            AutoStartManager.SetEnabled(autoStart.Checked);
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
        _timer.Interval = _settings.PollingIntervalMs;
        _settings.Save();
        // Refresh the menu so checkmarks reflect the new state next open.
        _tray.ContextMenuStrip = BuildMenu();
    }

    private void ExitApp()
    {
        _logger.Log("---- HanEngIndicator exiting ----");
        _timer.Stop();
        _tray.Visible = false;
        _overlay.HideBadge();
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _tray.Dispose();
            _trayIconHandle?.Dispose();
            _overlay.Dispose();
        }

        base.Dispose(disposing);
    }
}
