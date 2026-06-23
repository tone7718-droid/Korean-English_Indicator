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
            ConversionProbe probe = ProbeConversionMode(foreground);
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

        var snapshot = new InputStateSnapshot(
            mode, koreanLayout, imeOpen, layoutId, className, threadId);

        if (_logger.Enabled)
        {
            _logger.Log(string.Create(CultureInfo.InvariantCulture,
                $"IME  class='{className}' tid={threadId} hkl=0x{layoutId:X4} " +
                $"koreanLayout={koreanLayout} open={imeOpen} mode={mode}"));
        }

        return snapshot;
    }

    private readonly record struct ConversionProbe(bool HasValue, bool Open, int ConversionMode);

    private ConversionProbe ProbeConversionMode(IntPtr foreground)
    {
        // --- Primary: WM_IME_CONTROL to the default IME window ---
        IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(foreground);
        if (imeWnd != IntPtr.Zero)
        {
            bool gotMode = TrySendImeControl(imeWnd, NativeMethods.IMC_GETCONVERSIONMODE, out int conv);
            bool gotOpen = TrySendImeControl(imeWnd, NativeMethods.IMC_GETOPENSTATUS, out int open);

            if (gotMode)
            {
                // If open status is unavailable, assume open (Korean MS-IME is
                // effectively always "open"; NATIVE bit carries the 가/A state).
                bool open01 = !gotOpen || open != 0;
                return new ConversionProbe(true, open01, conv);
            }
        }

        // --- Fallback: IMM context on the focused control ---
        var gui = new NativeMethods.GUITHREADINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        uint tid = NativeMethods.GetWindowThreadProcessId(foreground, out _);
        IntPtr target = foreground;
        if (NativeMethods.GetGUIThreadInfo(tid, ref gui) && gui.hwndFocus != IntPtr.Zero)
        {
            target = gui.hwndFocus;
        }

        IntPtr himc = NativeMethods.ImmGetContext(target);
        if (himc == IntPtr.Zero)
        {
            return new ConversionProbe(false, false, 0);
        }

        try
        {
            bool open = NativeMethods.ImmGetOpenStatus(himc);
            if (NativeMethods.ImmGetConversionStatus(himc, out int conversion, out _))
            {
                return new ConversionProbe(true, open, conversion);
            }
        }
        finally
        {
            NativeMethods.ImmReleaseContext(target, himc);
        }

        return new ConversionProbe(false, false, 0);
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
            120,
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
