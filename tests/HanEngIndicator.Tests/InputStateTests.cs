using HanEngIndicator.Models;
using Xunit;

namespace HanEngIndicator.Tests;

public class InputStateTests
{
    [Fact]
    public void Unknown_snapshot_has_unknown_mode_and_empty_metadata()
    {
        InputStateSnapshot s = InputStateSnapshot.Unknown;
        Assert.Equal(InputMode.Unknown, s.Mode);
        Assert.False(s.KoreanLayoutActive);
        Assert.False(s.ImeOpen);
        Assert.Equal(0, s.KeyboardLayoutId);
        Assert.Equal(string.Empty, s.ForegroundWindowClass);
    }

    [Fact]
    public void Snapshot_records_metadata_but_never_text()
    {
        var s = new InputStateSnapshot(
            InputMode.Korean, true, true, 0x0412, "Edit", 1234);

        Assert.Equal(InputMode.Korean, s.Mode);
        Assert.Equal(0x0412, s.KeyboardLayoutId);
        Assert.Equal("Edit", s.ForegroundWindowClass);
        Assert.Equal(1234u, s.ForegroundThreadId);
    }

    [Fact]
    public void CaretAnchor_none_has_no_value()
    {
        Assert.False(CaretAnchor.None.HasValue);
        Assert.Equal(CaretSource.None, CaretAnchor.None.Source);
    }
}
