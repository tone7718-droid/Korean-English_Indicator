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
                if (!caret.HasValue)
                {
                    caret = ThrottledUiaCaret();
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

    // UI Automation is comparatively expensive. Throttle it and reuse the last
    // result between attempts so that, for apps without a standard caret (e.g. a
    // custom chart control), we call UIA at most a few times per second instead
    // of every polling cycle. (This runs on the background worker thread, so it
    // never blocks the UI - the throttle is purely to keep the load light.)
    private const int UiaThrottleMs = 400;
    private CaretAnchor _lastUia = CaretAnchor.None;
    private DateTime _lastUiaAtUtc = DateTime.MinValue;

    private CaretAnchor ThrottledUiaCaret()
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _lastUiaAtUtc).TotalMilliseconds < UiaThrottleMs)
        {
            return _lastUia; // reuse cached anchor (may be None -> mouse fallback)
        }

        _lastUiaAtUtc = now;
        _lastUia = TryUiAutomationCaret();
        return _lastUia;
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
