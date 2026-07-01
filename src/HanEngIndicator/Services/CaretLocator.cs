using System.Drawing;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using HanEngIndicator.Models;
using HanEngIndicator.Native;
using HanEngIndicator.Settings;

namespace HanEngIndicator.Services;

/// <summary>
/// Finds where to anchor the overlay, in order of preference:
///   1. The text caret via GetGUIThreadInfo (Win32 edits, Notepad, many controls).
///   2. UI Automation TextPattern selection rectangle (browsers, custom controls).
///   3. UI Automation focused-element bounding rectangle.
///   4. The mouse pointer.
/// All coordinates returned are PHYSICAL screen pixels.
///
/// This class only reads geometry. It never reads the text content of any
/// control.
/// </summary>
public sealed class CaretLocator
{
    private readonly DiagnosticLogger _logger;

    public CaretLocator(DiagnosticLogger logger)
    {
        _logger = logger;
    }

    public CaretAnchor Locate(InputStateSnapshot snapshot, AppSettings settings)
    {
        switch (settings.PositionMode)
        {
            case PositionMode.FixedCorner:
                return CaretAnchor.None; // caller pins to a corner

            case PositionMode.Mouse:
                return MouseAnchor();

            case PositionMode.CaretThenMouse:
            default:
                CaretAnchor caret = TryGuiThreadCaret(snapshot.ForegroundThreadId);
                if (!caret.HasValue && settings.UseUiAutomation)
                {
                    caret = ThrottledUiaCaret(NativeMethods.GetForegroundWindow());
                }

                CaretAnchor result = caret.HasValue ? caret : MouseAnchor();

                if (_logger.Enabled)
                {
                    _logger.Log(string.Create(CultureInfo.InvariantCulture,
                        $"CARET source={result.Source} rect={result.ScreenRect}"));
                }

                return result;
        }
    }

    // --- 1. GetGUIThreadInfo -------------------------------------------------

    private static CaretAnchor TryGuiThreadCaret(uint threadId)
    {
        if (threadId == 0)
        {
            return CaretAnchor.None;
        }

        var gui = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>(),
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref gui))
        {
            return CaretAnchor.None;
        }

        if (gui.rcCaret.IsEmpty)
        {
            return CaretAnchor.None;
        }

        IntPtr host = gui.hwndCaret != IntPtr.Zero ? gui.hwndCaret : gui.hwndFocus;
        if (host == IntPtr.Zero)
        {
            return CaretAnchor.None;
        }

        var topLeft = new NativeMethods.POINT { X = gui.rcCaret.Left, Y = gui.rcCaret.Top };
        var bottomRight = new NativeMethods.POINT { X = gui.rcCaret.Right, Y = gui.rcCaret.Bottom };

        if (!NativeMethods.ClientToScreen(host, ref topLeft) ||
            !NativeMethods.ClientToScreen(host, ref bottomRight))
        {
            return CaretAnchor.None;
        }

        var rect = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return new CaretAnchor(rect, CaretSource.GuiThreadInfoCaret);
    }

    // --- 2/3. UI Automation --------------------------------------------------

    // UI Automation is comparatively expensive AND can block indefinitely if a
    // provider is unresponsive. So we run it on a background task with a BOUNDED
    // wait, keep at most ONE call in flight (a stuck provider can never pile up
    // threads or stall the detection worker), throttle attempts, cache the last
    // result keyed by the foreground window, and gate it with a circuit breaker.
    private const int UiaThrottleMs = 400;  // minimum gap between UIA attempts
    private const int UiaWaitMs = 180;      // max time the worker waits for UIA
    private const int UiaSlowStreakLimit = 3;
    private static readonly TimeSpan UiaCooldown = TimeSpan.FromSeconds(30);

    private readonly UiaCircuitBreaker _uiaBreaker = new(UiaSlowStreakLimit, UiaCooldown);
    private CaretAnchor _lastUia = CaretAnchor.None;
    private DateTime _lastUiaAtUtc = DateTime.MinValue;
    private IntPtr _lastUiaHwnd;
    private Task<CaretAnchor>? _uiaInFlight;
    private IntPtr _inFlightHwnd;

    private CaretAnchor ThrottledUiaCaret(IntPtr foregroundWindow)
    {
        DateTime now = DateTime.UtcNow;

        // Circuit breaker: skip UIA entirely during a cooldown.
        if (_uiaBreaker.IsOpen(now))
        {
            return CaretAnchor.None;
        }

        // Invalidate the cache immediately when the foreground WINDOW changes
        // (not just the thread), so we never reuse another window's - or another
        // control's within the same thread - caret coordinates.
        if (foregroundWindow != _lastUiaHwnd)
        {
            _lastUiaHwnd = foregroundWindow;
            _lastUia = CaretAnchor.None;
            _lastUiaAtUtc = DateTime.MinValue;
        }

        // Harvest a previously-started call if it has since completed. Discard a
        // late result if the foreground window changed while it ran (stale).
        if (_uiaInFlight is { IsCompleted: true } finished)
        {
            _lastUia = (finished.Status == TaskStatus.RanToCompletion && _inFlightHwnd == foregroundWindow)
                ? finished.Result
                : CaretAnchor.None;
            _lastUiaAtUtc = now;
            _uiaInFlight = null;
        }
        else if (_uiaInFlight is not null)
        {
            // A previous call is still running (unresponsive provider). Do NOT
            // start another. Crucially, do NOT keep returning the last UIA
            // coordinate - that would pin the badge to a stale spot for as long
            // as the provider stays stuck. Return None so the caller falls back
            // to the mouse, and count this stuck cycle as unhealthy so the
            // breaker actually opens on a persistent hang (the first timeout
            // alone is not enough, since we early-return here every cycle after).
            _uiaBreaker.Record(healthy: false, now);
            return CaretAnchor.None;
        }

        // Throttle: within the window, reuse the cached anchor.
        if ((now - _lastUiaAtUtc).TotalMilliseconds < UiaThrottleMs)
        {
            return _lastUia;
        }

        // Start a bounded UIA call on a background task.
        _lastUiaAtUtc = now;
        _inFlightHwnd = foregroundWindow;
        Task<CaretAnchor> task = Task.Run(TryUiAutomationCaret);

        bool completed;
        try
        {
            completed = task.Wait(UiaWaitMs);
        }
        catch
        {
            completed = true; // faulted within the wait; treat as no result
        }

        if (completed)
        {
            _uiaInFlight = null;
            _lastUia = task.Status == TaskStatus.RanToCompletion ? task.Result : CaretAnchor.None;
            _uiaBreaker.Record(healthy: true, now);
            return _lastUia;
        }

        // Timed out: keep the task tracked so we never start a second one, mark
        // the attempt unhealthy (cooldown measured from NOW, not the call start),
        // and fall back to the mouse. Return None (NOT the last UIA coordinate) so
        // this cycle is a real mouse fallback - the badge must never stay frozen
        // on a stale caret while UIA is stuck.
        _uiaInFlight = task;
        _uiaBreaker.Record(healthy: false, DateTime.UtcNow);
        if (_logger.Enabled)
        {
            _logger.Log("UIA call exceeded budget; using mouse fallback this cycle.");
        }

        return CaretAnchor.None;
    }

    // Reject empty / zero-height / absurd rectangles so the badge never jumps to
    // a bogus coordinate (e.g. a 0x0 selection rect reported at the origin).
    private const int CoordLimit = 100_000;
    private static bool SaneCoord(int v) => v is > -CoordLimit and < CoordLimit;
    private static bool IsSaneCaretRect(Rectangle r) =>
        r.Height > 0 && r.Width >= 0 && SaneCoord(r.X) && SaneCoord(r.Y);

    private CaretAnchor TryUiAutomationCaret()
    {
        try
        {
            AutomationElement? focused = AutomationElement.FocusedElement;
            if (focused is null)
            {
                return CaretAnchor.None;
            }

            // Prefer the precise caret/selection rectangle from TextPattern.
            if (focused.TryGetCurrentPattern(TextPattern.Pattern, out object patternObj)
                && patternObj is TextPattern textPattern)
            {
                Rectangle? textRect = FirstSelectionRectangle(textPattern);
                if (textRect is { } tr && IsSaneCaretRect(tr))
                {
                    return new CaretAnchor(tr, CaretSource.UiAutomationText);
                }
            }

            // Fall back to the focused element's bounding rectangle.
            var bounds = focused.Current.BoundingRectangle;
            if (!bounds.IsEmpty && bounds.Width > 0 && bounds.Height > 0)
            {
                var rect = new Rectangle(
                    (int)Math.Round(bounds.Left),
                    (int)Math.Round(bounds.Top),
                    (int)Math.Round(bounds.Width),
                    (int)Math.Round(bounds.Height));

                if (SaneCoord(rect.X) && SaneCoord(rect.Y))
                {
                    // Anchor to the lower-left so the badge does not cover the field.
                    var anchor = new Rectangle(rect.Left, rect.Bottom, 0, 0);
                    return new CaretAnchor(anchor, CaretSource.UiAutomationBoundingRect);
                }
            }
        }
        catch
        {
            // UI Automation can throw for protected/unsupported windows.
            // Degrade gracefully to the mouse fallback.
        }

        return CaretAnchor.None;
    }

    private static Rectangle? FirstSelectionRectangle(TextPattern textPattern)
    {
        try
        {
            TextPatternRange[] ranges = textPattern.GetSelection();
            if (ranges is { Length: > 0 })
            {
                TextPatternRange range = ranges[0];
                System.Windows.Rect[] rects = range.GetBoundingRectangles();
                if (rects.Length > 0 && rects[0].Height > 0)
                {
                    System.Windows.Rect r = rects[0];
                    return new Rectangle(
                        (int)Math.Round(r.X),
                        (int)Math.Round(r.Y),
                        (int)Math.Round(r.Width),
                        (int)Math.Round(r.Height));
                }
            }
        }
        catch
        {
            // ignore and let the caller fall through
        }

        return null;
    }

    // --- 4. Mouse pointer ----------------------------------------------------

    private static CaretAnchor MouseAnchor()
    {
        if (NativeMethods.GetCursorPos(out NativeMethods.POINT pt))
        {
            // Small box approximating the pointer so the badge clears the arrow.
            var rect = new Rectangle(pt.X, pt.Y, 6, 16);
            return new CaretAnchor(rect, CaretSource.MousePointer);
        }

        return CaretAnchor.None;
    }
}
