using System.Globalization;
using HanEngIndicator.Models;
using HanEngIndicator.Native;

namespace HanEngIndicator.Services;

/// <summary>
/// Detects the REAL Korean-IME sub-state (가 / A), not merely the keyboard
/// layout shown on the taskbar.
///
/// Strategy (most reliable first):
///  1. Resolve the foreground window and its UI thread.
///  2. Read the keyboard layout for that thread. If it is not Korean, the input
///     is alphanumeric -> English.
///  3. Ask the IME for its conversion mode. The primary, broadly-compatible
///     method is WM_IME_CONTROL/IMC_GETCONVERSIONMODE sent to the default IME
///     window (works for Win32 edits, browsers, and many custom controls).
///     The IME_CMODE_NATIVE bit distinguishes Hangul (가) from alpha (A).
///  4. Fall back to ImmGetContext + ImmGetConversionStatus on the focused
///     control if the message route yields nothing.
/// </summary>
public sealed class ImeDetector
{
    private readonly DiagnosticLogger _logger;

    // Caps Lock read throttle (worker thread only). ~300ms keeps A/a responsive
    // while avoiding an AttachThreadInput on every 120ms cycle.
    private const int CapsPollMs = 300;
    private bool _lastCaps;
    private DateTime _lastCapsAtUtc = DateTime.MinValue;

    public ImeDetector(DiagnosticLogger logger)
    {
        _logger = logger;
    }

    public InputStateSnapshot Detect()
    {
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            return InputStateSnapshot.Unknown;
        }

        uint threadId = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        string className = NativeMethods.GetWindowClassName(foreground);

        IntPtr hkl = NativeMethods.GetKeyboardLayout(threadId);
        int layoutId = (int)((long)hkl & 0xFFFF);
        bool koreanLayout = layoutId == NativeMethods.LANG_KOREAN;

        bool imeOpen = false;
        InputMode mode;

        if (!koreanLayout)
        {
            // Non-Korean layout: typing produces Latin characters.
            mode = InputMode.English;
        }
        else
        {
            ConversionProbe probe = ProbeConversionMode(foreground, threadId);
            imeOpen = probe.Open;

            if (!probe.HasValue)
            {
                mode = InputMode.Unknown;
            }
            else if (!probe.Open)
            {
                // IME closed -> alphanumeric.
                mode = InputMode.English;
            }
            else
            {
                mode = (probe.ConversionMode & NativeMethods.IME_CMODE_NATIVE) != 0
                    ? InputMode.Korean
                    : InputMode.English;
            }
        }

        // Only meaningful for English input: Caps Lock decides A (upper) vs a (lower).
        // Reading it requires briefly attaching to the foreground thread's input
        // (see NativeMethods), which we throttle: Caps Lock changes rarely, so we
        // re-read at most every CapsPollMs and reuse the cached value otherwise.
        bool capsLock = false;
        if (mode == InputMode.English)
        {
            DateTime nowCaps = DateTime.UtcNow;
            if ((nowCaps - _lastCapsAtUtc).TotalMilliseconds >= CapsPollMs)
            {
                bool reading = NativeMethods.ReadCapsLock(threadId, foreground, out bool confident);

                // Only adopt a CONFIDENT read (we actually attached to the
                // foreground input). A non-confident read may be reset/stale, so
                // keep the last known value rather than risk showing a wrong 'a'.
                if (confident)
                {
                    _lastCaps = reading;
                }

                _lastCapsAtUtc = nowCaps; // throttle attempts either way (avoid churn)
            }

            capsLock = _lastCaps;
        }

        var snapshot = new InputStateSnapshot(
            mode, koreanLayout, imeOpen, layoutId, className, threadId, capsLock);

        if (_logger.Enabled)
        {
            _logger.Log(string.Create(CultureInfo.InvariantCulture,
                $"IME  class='{className}' tid={threadId} hkl=0x{layoutId:X4} " +
                $"koreanLayout={koreanLayout} open={imeOpen} mode={mode}"));
        }

        return snapshot;
    }

    private readonly record struct ConversionProbe(bool HasValue, bool Open, int ConversionMode);

    private ConversionProbe ProbeConversionMode(IntPtr foreground, uint threadId)
    {
        // --- Primary: WM_IME_CONTROL to the default IME window ---
        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(foreground);
        bool sentOk = false;
        if (imeWnd != IntPtr.Zero)
        {
            bool gotMode = TrySendImeControl(imeWnd, NativeMethods.IMC_GETCONVERSIONMODE, out int conv);
            sentOk = gotMode;

            if (gotMode)
            {
                // Korean MS-IME is effectively always "open"; the NATIVE bit
                // carries the 가/A state. We deliberately skip the extra
                // IMC_GETOPENSTATUS round-trip to halve the blocking cost.
                return new ConversionProbe(true, true, conv);
            }
        }

        // --- Fallback: IMM context on the focused control ---
        // GetGUIThreadInfo success/failure on the foreground thread is a strong
        // signal: if it fails, our process most likely cannot reach the target
        // window because the target runs at a higher integrity level
        // (i.e. the foreground app is elevated / "관리자 권한"), and Windows UIPI
        // blocks cross-integrity access.
        var gui = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        bool guiOk = NativeMethods.GetGUIThreadInfo(threadId, ref gui);
        IntPtr target = (guiOk && gui.hwndFocus != IntPtr.Zero) ? gui.hwndFocus : foreground;

        IntPtr himc = NativeMethods.ImmGetContext(target);
        ConversionProbe result;
        if (himc == IntPtr.Zero)
        {
            result = new ConversionProbe(false, false, 0);
        }
        else
        {
            bool open = NativeMethods.ImmGetOpenStatus(himc);
            if (NativeMethods.ImmGetConversionStatus(himc, out int conversion, out _))
            {
                result = new ConversionProbe(true, open, conversion);
            }
            else
            {
                result = new ConversionProbe(false, false, 0);
            }

            NativeMethods.ImmReleaseContext(target, himc);
        }

        if (_logger.Enabled)
        {
            string hint = (!result.HasValue && !sentOk && !guiOk)
                ? "  <-- likely the foreground app is ELEVATED (UIPI block). Try running this app as administrator."
                : (!result.HasValue ? "  <-- IME state not exposed by this control (non-standard/TSF control)." : string.Empty);

            _logger.Log(string.Create(CultureInfo.InvariantCulture,
                $"PROBE imeWnd={(imeWnd != IntPtr.Zero ? "yes" : "no")} wmImeControl={(sentOk ? "ok" : "fail")} " +
                $"guiThreadInfo={(guiOk ? "ok" : "fail")} immContext={(himc != IntPtr.Zero ? "yes" : "no")} " +
                $"result={(result.HasValue ? "ok" : "none")}{hint}"));
        }

        return result;
    }

    private static bool TrySendImeControl(IntPtr imeWnd, int command, out int result)
    {
        result = 0;
        IntPtr ret = NativeMethods.SendMessageTimeout(
            imeWnd,
            NativeMethods.WM_IME_CONTROL,
            new IntPtr(command),
            IntPtr.Zero,
            NativeMethods.SMTO_ABORTIFHUNG,
            80,
            out IntPtr res);

        if (ret == IntPtr.Zero)
        {
            // Timed out or the window did not handle the message.
            return false;
        }

        result = res.ToInt32();
        return true;
    }
}
