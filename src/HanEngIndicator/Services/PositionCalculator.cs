using System.Drawing;

namespace HanEngIndicator.Services;

/// <summary>
/// Pure geometry for placing the overlay. All values are in PHYSICAL pixels so
/// the same math works on every DPI scale. Kept free of any WinForms/Win32
/// dependency so it can be unit-tested on any OS.
/// </summary>
public static class PositionCalculator
{
    /// <summary>Base badge edge length at 96 DPI before user font scaling.</summary>
    public const int BaseBadgeSize = 28;

    /// <summary>
    /// Compute the badge edge length in physical pixels for a given monitor DPI
    /// and user scale.
    /// </summary>
    public static int BadgeSizeForDpi(uint dpi, double fontScale)
    {
        if (dpi == 0)
        {
            dpi = 96;
        }

        double px = BaseBadgeSize * (dpi / 96.0) * fontScale;
        return (int)Math.Round(Math.Clamp(px, 16, 200));
    }

    /// <summary>Scale a logical (96-DPI) offset to physical pixels.</summary>
    public static Point ScaleOffset(Point logicalOffset, uint dpi)
    {
        if (dpi == 0)
        {
            dpi = 96;
        }

        double f = dpi / 96.0;
        return new Point(
            (int)Math.Round(logicalOffset.X * f),
            (int)Math.Round(logicalOffset.Y * f));
    }

    /// <summary>
    /// Place the badge to the lower-right of <paramref name="anchorRect"/>,
    /// offset by <paramref name="offset"/>, then keep it fully inside
    /// <paramref name="workArea"/>. If it would overflow an edge it flips to the
    /// opposite side of the anchor where possible, otherwise it is clamped.
    /// </summary>
    public static Point ComputeTopLeft(Rectangle anchorRect, Size badgeSize, Point offset, Rectangle workArea)
    {
        int x = anchorRect.Right + offset.X;
        int y = anchorRect.Bottom + offset.Y;

        // Flip horizontally to the left of the anchor if it overflows the right edge.
        if (x + badgeSize.Width > workArea.Right)
        {
            int flipped = anchorRect.Left - offset.X - badgeSize.Width;
            x = flipped >= workArea.Left ? flipped : workArea.Right - badgeSize.Width;
        }

        // Flip vertically above the anchor if it overflows the bottom edge.
        if (y + badgeSize.Height > workArea.Bottom)
        {
            int flipped = anchorRect.Top - offset.Y - badgeSize.Height;
            y = flipped >= workArea.Top ? flipped : workArea.Bottom - badgeSize.Height;
        }

        // Final hard clamp (covers anchors at/over the work-area edges).
        x = Math.Clamp(x, workArea.Left, Math.Max(workArea.Left, workArea.Right - badgeSize.Width));
        y = Math.Clamp(y, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - badgeSize.Height));

        return new Point(x, y);
    }

    /// <summary>Position for a fixed-corner placement within a work area.</summary>
    public static Point ComputeCorner(Settings.ScreenCorner corner, Size badgeSize, Point margin, Rectangle workArea)
    {
        int left = workArea.Left + margin.X;
        int top = workArea.Top + margin.Y;
        int right = workArea.Right - badgeSize.Width - margin.X;
        int bottom = workArea.Bottom - badgeSize.Height - margin.Y;

        return corner switch
        {
            Settings.ScreenCorner.TopLeft => new Point(left, top),
            Settings.ScreenCorner.TopRight => new Point(right, top),
            Settings.ScreenCorner.BottomLeft => new Point(left, bottom),
            _ => new Point(right, bottom),
        };
    }
}
