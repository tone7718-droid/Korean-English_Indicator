using System.Runtime.InteropServices;

namespace HanEngIndicator.Native;

/// <summary>
/// P/Invoke declarations. This app only ever READS window / input-method state
/// and positions a borderless overlay. It never reads typed characters,
/// clipboard contents, screen pixels, or any patient data.
/// </summary>
internal static class NativeMethods
{
    // ---- Window / thread / focus ------------------------------------------

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, char[] lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll")]
    public static extern short GetKeyState(int nVirtKey);

    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    /// <summary>Virtual key code for Caps Lock.</summary>
    public const int VK_CAPITAL = 0x14;

    /// <summary>
    /// True when Caps Lock is toggled on.
    ///
    /// GetKeyState reflects the CALLING thread's message-queue key state, which
    /// is stale on our background worker (it pumps no keyboard messages). To read
    /// the real toggle we briefly attach to the foreground thread's input queue -
    /// per Microsoft, "threads connected through AttachThreadInput share the same
    /// keyboard state" - then detach immediately. If attaching fails (different
    /// integrity/desktop), we fall back to a best-effort direct read.
    /// </summary>
    public static bool IsCapsLockOn(uint foregroundThreadId)
    {
        uint self = GetCurrentThreadId();
        bool attached = foregroundThreadId != 0
            && foregroundThreadId != self
            && AttachThreadInput(self, foregroundThreadId, true);
        try
        {
            return (GetKeyState(VK_CAPITAL) & 0x0001) != 0;
        }
        finally
        {
            if (attached)
            {
                AttachThreadInput(self, foregroundThreadId, false);
            }
        }
    }

    // ---- IME (IMM32) -------------------------------------------------------

    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("imm32.dll")]
    public static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    public static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    public static extern bool ImmGetConversionStatus(IntPtr hIMC, out int lpfdwConversion, out int lpfdwSentence);

    [DllImport("imm32.dll")]
    public static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    // ---- Caret / GUI thread info ------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;
        public readonly int Height => Bottom - Top;
        public readonly bool IsEmpty => Left == 0 && Top == 0 && Right == 0 && Bottom == 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO lpgui);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // ---- Z-order (keep overlay above other top-most windows) --------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;

    // ---- Icon lifetime (avoid GDI handle leaks) ---------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    // ---- DPI ---------------------------------------------------------------

    public const int MONITOR_DEFAULTTONEAREST = 2;
    public enum MonitorDpiType { Effective = 0, Angular = 1, Raw = 2 }

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, int dwFlags);

    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, MonitorDpiType dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// Preferred DPI query for a Per-Monitor-v2 process: returns the DPI of the
    /// monitor hosting the given window. Available on Windows 10 1607+.
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);

    // ---- Layered / click-through window styles ----------------------------

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_TOPMOST = 0x00000008;

    // SendMessageTimeout flags
    public const uint SMTO_ABORTIFHUNG = 0x0002;

    // WM_IME_CONTROL
    public const uint WM_IME_CONTROL = 0x0283;
    public const int IMC_GETCONVERSIONMODE = 0x0001;
    public const int IMC_GETOPENSTATUS = 0x0005;

    // IME conversion mode bits
    public const int IME_CMODE_NATIVE = 0x0001;     // Hangul (가) when set, alpha (A) when clear
    public const int IME_CMODE_FULLSHAPE = 0x0008;

    // Korean keyboard layout primary language id (LANG_KOREAN).
    public const int LANG_KOREAN = 0x0412;

    /// <summary>Read the foreground window's class name without allocating per call paths.</summary>
    public static string GetWindowClassName(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero)
        {
            return string.Empty;
        }

        var buffer = new char[256];
        int len = GetClassName(hWnd, buffer, buffer.Length);
        return len > 0 ? new string(buffer, 0, len) : string.Empty;
    }
}
