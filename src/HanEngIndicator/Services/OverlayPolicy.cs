using HanEngIndicator.Models;
using HanEngIndicator.Settings;

namespace HanEngIndicator.Services;

/// <summary>
/// The effective display decision for one cycle: whether to show the badge and,
/// if so, with which glyph (Mode + CapsLock). Position is intentionally NOT part
/// of this decision - it must always be recomputed from the CURRENT foreground
/// window so a transient Unknown never makes the badge track a stale window.
/// </summary>
public readonly record struct OverlayDecision(bool Show, InputMode Mode, bool CapsLock);

/// <summary>
/// Pure decision logic (no Win32/WinForms dependency) so it can be unit-tested.
/// Applies the transient-Unknown hysteresis and the display policy.
/// </summary>
public static class OverlayPolicy
{
    /// <summary>
    /// Decide what to display this cycle.
    /// </summary>
    /// <param name="current">The freshly detected state.</param>
    /// <param name="lastGoodMode">Last non-Unknown mode seen.</param>
    /// <param name="lastGoodCaps">Caps Lock state captured with the last good mode.</param>
    /// <param name="lastGoodAtUtc">When the last good mode was seen.</param>
    /// <param name="nowUtc">Current time.</param>
    /// <param name="graceMs">How long a transient Unknown may reuse the last good mode.</param>
    /// <param name="displayPolicy">Always vs Korean-only.</param>
    public static OverlayDecision Decide(
        InputStateSnapshot current,
        InputMode lastGoodMode,
        bool lastGoodCaps,
        DateTime lastGoodAtUtc,
        DateTime nowUtc,
        int graceMs,
        DisplayPolicy displayPolicy)
    {
        InputMode mode = current.Mode;
        bool caps = current.CapsLock;

        // A single transient Unknown (window/dialog switch, or an IME query that
        // briefly timed out) should not blink the badge off: reuse the last good
        // GLYPH (mode + caps) for a short grace period. We deliberately do NOT
        // reuse any position/thread info from the past.
        if (mode == InputMode.Unknown &&
            lastGoodMode != InputMode.Unknown &&
            (nowUtc - lastGoodAtUtc).TotalMilliseconds <= graceMs)
        {
            mode = lastGoodMode;
            caps = lastGoodCaps;
        }

        bool show = mode switch
        {
            InputMode.Korean => true,
            InputMode.English => displayPolicy == DisplayPolicy.Always,
            _ => false,
        };

        return new OverlayDecision(show, mode, caps);
    }
}
