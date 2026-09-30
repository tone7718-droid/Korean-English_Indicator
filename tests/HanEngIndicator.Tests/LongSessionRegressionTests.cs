using HanEngIndicator.Models;
using HanEngIndicator.Services;
using HanEngIndicator.Settings;
using Xunit;

namespace HanEngIndicator.Tests;

public class LongSessionRegressionTests
{
    [Fact]
    public void Blocked_ui_retains_only_latest_of_a_million_updates()
    {
        var mailbox = new LatestValueMailbox<int>();
        int callbacks = 0;
        Parallel.For(0, 1_000_000, i =>
        {
            if (mailbox.Publish(i)) Interlocked.Increment(ref callbacks);
        });
        Assert.Equal(1, callbacks);
        Assert.False(mailbox.Publish(1_000_000));
        Assert.Equal(1_000_000, mailbox.Take());
        Assert.True(mailbox.Publish(1_000_001));
        Assert.Equal(1_000_001, mailbox.Take());
    }

    [Theory]
    [InlineData(20, 11)]
    [InlineData(10, 12)]
    public void Unknown_in_another_window_or_control_does_not_reuse_old_mode(int window, int focus)
    {
        var now = DateTime.UtcNow;
        var current = new InputStateSnapshot(InputMode.Unknown, true, false, 0x0412, "Edit", 100,
            false, new IntPtr(window), new IntPtr(focus));
        var decision = OverlayPolicy.Decide(current, InputMode.Korean, false, now.AddMilliseconds(-100),
            now, 800, DisplayPolicy.Always, new IntPtr(10), new IntPtr(11));
        Assert.False(decision.Show);
    }

    [Fact]
    public void Unknown_in_same_control_can_use_short_grace_period()
    {
        var now = DateTime.UtcNow;
        var current = new InputStateSnapshot(InputMode.Unknown, true, false, 0x0412, "Edit", 100,
            false, new IntPtr(10), new IntPtr(11));
        Assert.True(OverlayPolicy.Decide(current, InputMode.Korean, false, now.AddMilliseconds(-100),
            now, 800, DisplayPolicy.Always, new IntPtr(10), new IntPtr(11)).Show);
    }

    [Fact]
    public void Damaged_settings_recover_last_valid_backup()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new AppSettings { OffsetX = 20 };
            Assert.True(settings.Save(path));
            settings.OffsetX = 30;
            Assert.True(settings.Save(path));
            Assert.Equal(30, AppSettings.Load(path).OffsetX);
            File.WriteAllText(path, "{broken");
            Assert.Equal(20, AppSettings.Load(path).OffsetX);
            Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Failed_save_reports_failure_and_does_not_change_existing_settings()
    {
        string directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = Path.Combine(directory, "settings.json");
        try
        {
            var settings = new AppSettings { OffsetX = 20 };
            Assert.True(settings.Save(path));
            Assert.False(settings.Save(Path.Combine(path, "impossible.json")));
            Assert.Equal(20, AppSettings.Load(path).OffsetX);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void Invalid_settings_values_return_to_safe_defaults()
    {
        var settings = new AppSettings
        {
            FontScale = double.NaN, Opacity = double.PositiveInfinity,
            PositionMode = (PositionMode)99, FixedCorner = (ScreenCorner)99,
            DisplayPolicy = (DisplayPolicy)99,
        }.Clamped();
        Assert.Equal(1, settings.FontScale);
        Assert.Equal(1, settings.Opacity);
        Assert.Equal(PositionMode.CaretThenMouse, settings.PositionMode);
        Assert.Equal(ScreenCorner.BottomRight, settings.FixedCorner);
        Assert.Equal(DisplayPolicy.Always, settings.DisplayPolicy);
    }
}
