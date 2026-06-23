using System.Drawing;

namespace HanEngIndicator.Models;

/// <summary>
/// How the caret (or anchor) position for the overlay was obtained.
/// Used for diagnostics only.
/// </summary>
public enum CaretSource
{
    None = 0,
    GuiThreadInfoCaret,
    UiAutomationText,
    UiAutomationBoundingRect,
    MousePointer,
    FixedCorner,
}

/// <summary>
/// The screen anchor (physical pixels) the overlay should attach to, plus how
/// it was found. The rectangle describes the caret/text area so the overlay can
/// be offset to its lower-right without covering it.
/// </summary>
public readonly record struct CaretAnchor(Rectangle ScreenRect, CaretSource Source)
{
    public bool HasValue => Source != CaretSource.None;

    public static CaretAnchor None { get; } = new(Rectangle.Empty, CaretSource.None);
}
