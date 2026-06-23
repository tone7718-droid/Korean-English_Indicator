using System.Drawing;
using HanEngIndicator.Services;
using HanEngIndicator.Settings;
using Xunit;

namespace HanEngIndicator.Tests;

public class PositionCalculatorTests
{
    [Theory]
    [InlineData(96u, 1.0, 28)]
    [InlineData(120u, 1.0, 35)]   // 125%
    [InlineData(144u, 1.0, 42)]   // 150%
    [InlineData(96u, 1.6, 45)]
    [InlineData(0u, 1.0, 28)]     // unknown dpi -> treated as 96
    public void BadgeSizeForDpi_scales_with_dpi_and_user_scale(uint dpi, double scale, int expected)
    {
        Assert.Equal(expected, PositionCalculator.BadgeSizeForDpi(dpi, scale));
    }

    [Fact]
    public void BadgeSizeForDpi_is_clamped_to_sane_bounds()
    {
        Assert.True(PositionCalculator.BadgeSizeForDpi(96, 0.01) >= 16);
        Assert.True(PositionCalculator.BadgeSizeForDpi(600, 5.0) <= 200);
    }

    [Theory]
    [InlineData(96u, 12, 12)]
    [InlineData(144u, 12, 18)]   // 150% -> 18
    public void ScaleOffset_scales_to_physical_pixels(uint dpi, int logical, int expected)
    {
        Point p = PositionCalculator.ScaleOffset(new Point(logical, logical), dpi);
        Assert.Equal(expected, p.X);
        Assert.Equal(expected, p.Y);
    }

    [Fact]
    public void ComputeTopLeft_places_badge_lower_right_of_caret()
    {
        var work = new Rectangle(0, 0, 1920, 1080);
        var caret = new Rectangle(500, 400, 2, 18);
        var size = new Size(28, 28);
        var offset = new Point(12, 10);

        Point p = PositionCalculator.ComputeTopLeft(caret, size, offset, work);

        Assert.Equal(caret.Right + 12, p.X);
        Assert.Equal(caret.Bottom + 10, p.Y);
    }

    [Fact]
    public void ComputeTopLeft_flips_left_when_overflowing_right_edge()
    {
        var work = new Rectangle(0, 0, 1920, 1080);
        var caret = new Rectangle(1915, 400, 2, 18);
        var size = new Size(28, 28);
        var offset = new Point(12, 10);

        Point p = PositionCalculator.ComputeTopLeft(caret, size, offset, work);

        // Must remain fully on screen.
        Assert.True(p.X >= work.Left);
        Assert.True(p.X + size.Width <= work.Right);
    }

    [Fact]
    public void ComputeTopLeft_flips_up_when_overflowing_bottom_edge()
    {
        var work = new Rectangle(0, 0, 1920, 1080);
        var caret = new Rectangle(500, 1075, 2, 18);
        var size = new Size(28, 28);
        var offset = new Point(12, 10);

        Point p = PositionCalculator.ComputeTopLeft(caret, size, offset, work);

        Assert.True(p.Y >= work.Top);
        Assert.True(p.Y + size.Height <= work.Bottom);
    }

    [Fact]
    public void ComputeTopLeft_respects_secondary_monitor_offset_origin()
    {
        // A right-hand monitor whose work area starts at x=1920.
        var work = new Rectangle(1920, 0, 1920, 1080);
        var caret = new Rectangle(2000, 200, 2, 18);
        var size = new Size(28, 28);
        var offset = new Point(12, 10);

        Point p = PositionCalculator.ComputeTopLeft(caret, size, offset, work);

        Assert.True(p.X >= work.Left);
        Assert.True(p.X + size.Width <= work.Right);
        Assert.Equal(caret.Right + 12, p.X);
    }

    [Theory]
    [InlineData(ScreenCorner.BottomRight)]
    [InlineData(ScreenCorner.BottomLeft)]
    [InlineData(ScreenCorner.TopRight)]
    [InlineData(ScreenCorner.TopLeft)]
    public void ComputeCorner_keeps_badge_inside_work_area(ScreenCorner corner)
    {
        var work = new Rectangle(0, 0, 1920, 1080);
        var size = new Size(40, 40);
        var margin = new Point(8, 8);

        Point p = PositionCalculator.ComputeCorner(corner, size, margin, work);

        Assert.InRange(p.X, work.Left, work.Right - size.Width);
        Assert.InRange(p.Y, work.Top, work.Bottom - size.Height);
    }
}
