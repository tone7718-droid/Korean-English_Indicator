namespace HanEngIndicator.Models;

/// <summary>
/// The detected input mode of the foreground window/control.
/// </summary>
public enum InputMode
{
    /// <summary>State could not be determined.</summary>
    Unknown = 0,

    /// <summary>Korean IME is active and in Hangul (가) mode.</summary>
    Korean,

    /// <summary>
    /// English/alphanumeric input. Either the keyboard layout is not Korean,
    /// or the Korean IME is in its English (A) sub-mode.
    /// </summary>
    English,
}

/// <summary>
/// Immutable snapshot of one detection cycle. Contains only non-sensitive
/// metadata - never any typed characters.
/// </summary>
public readonly record struct InputStateSnapshot(
    InputMode Mode,
    bool KoreanLayoutActive,
    bool ImeOpen,
    int KeyboardLayoutId,
    string ForegroundWindowClass,
    uint ForegroundThreadId,
    bool CapsLock = false)
{
    public static InputStateSnapshot Unknown { get; } = new(
        InputMode.Unknown, false, false, 0, string.Empty, 0, false);
}

/// <summary>
/// Maps an input state to the glyph shown on the badge. Pure and side-effect
/// free so it can be unit-tested.
///   Korean              -> "가"
///   English + Caps Lock -> "A"  (uppercase)
///   English (no Caps)   -> "a"  (lowercase)
///   Unknown             -> ""   (badge hidden)
/// </summary>
public static class BadgeText
{
    public static string Glyph(InputMode mode, bool capsLock) => mode switch
    {
        InputMode.Korean => "가",
        InputMode.English => capsLock ? "A" : "a",
        _ => string.Empty,
    };
}
