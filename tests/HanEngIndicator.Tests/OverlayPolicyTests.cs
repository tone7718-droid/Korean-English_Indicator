using HanEngIndicator.Models;
using HanEngIndicator.Services;
using HanEngIndicator.Settings;
using Xunit;

namespace HanEngIndicator.Tests;

public class OverlayPolicyTests
{
    private static InputStateSnapshot Snap(InputMode mode, bool caps = false, uint tid = 100) =>
        new(mode, mode == InputMode.Korean, mode == InputMode.Korean, 0x0412, "Edit", tid, caps);

    private static readonly DateTime Now = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Korean_always_shows()
    {
        var d = OverlayPolicy.Decide(Snap(InputMode.Korean), InputMode.Unknown, false,
            DateTime.MinValue, Now, 800, DisplayPolicy.Always);

        Assert.True(d.Show);
        Assert.Equal(InputMode.Korean, d.Mode);
    }

    [Fact]
    public void English_shows_only_under_Always_policy()
    {
        var always = OverlayPolicy.Decide(Snap(InputMode.English), InputMode.Unknown, false,
            DateTime.MinValue, Now, 800, DisplayPolicy.Always);
        var koreanOnly = OverlayPolicy.Decide(Snap(InputMode.English), InputMode.Unknown, false,
            DateTime.MinValue, Now, 800, DisplayPolicy.KoreanOnly);

        Assert.True(always.Show);
        Assert.False(koreanOnly.Show);
    }

    [Fact]
    public void Transient_unknown_within_grace_reuses_last_good_glyph()
    {
        // Last good was Korean 0.3s ago; current is a transient Unknown.
        var d = OverlayPolicy.Decide(
            Snap(InputMode.Unknown),
            lastGoodMode: InputMode.Korean, lastGoodCaps: false,
            lastGoodAtUtc: Now.AddMilliseconds(-300),
            nowUtc: Now, graceMs: 800, DisplayPolicy.Always);

        Assert.True(d.Show);
        Assert.Equal(InputMode.Korean, d.Mode);
    }

    [Fact]
    public void Transient_unknown_reuses_last_good_caps_lock_state()
    {
        var d = OverlayPolicy.Decide(
            Snap(InputMode.Unknown),
            lastGoodMode: InputMode.English, lastGoodCaps: true,
            lastGoodAtUtc: Now.AddMilliseconds(-100),
            nowUtc: Now, graceMs: 800, DisplayPolicy.Always);

        Assert.True(d.Show);
        Assert.Equal(InputMode.English, d.Mode);
        Assert.True(d.CapsLock); // 'A' preserved, not reset to 'a'
    }

    [Fact]
    public void Unknown_after_grace_expires_hides()
    {
        var d = OverlayPolicy.Decide(
            Snap(InputMode.Unknown),
            lastGoodMode: InputMode.Korean, lastGoodCaps: false,
            lastGoodAtUtc: Now.AddMilliseconds(-1200), // older than grace
            nowUtc: Now, graceMs: 800, DisplayPolicy.Always);

        Assert.False(d.Show);
        Assert.Equal(InputMode.Unknown, d.Mode);
    }

    [Fact]
    public void Unknown_with_no_prior_good_state_hides()
    {
        var d = OverlayPolicy.Decide(
            Snap(InputMode.Unknown),
            lastGoodMode: InputMode.Unknown, lastGoodCaps: false,
            lastGoodAtUtc: DateTime.MinValue, nowUtc: Now, graceMs: 800, DisplayPolicy.Always);

        Assert.False(d.Show);
    }

    [Fact]
    public void Fresh_english_overrides_stale_korean_good_state()
    {
        // Even though last good was Korean, a real English reading must win
        // immediately (no stale glyph) - grace only applies to Unknown.
        var d = OverlayPolicy.Decide(
            Snap(InputMode.English, caps: false),
            lastGoodMode: InputMode.Korean, lastGoodCaps: false,
            lastGoodAtUtc: Now.AddMilliseconds(-50),
            nowUtc: Now, graceMs: 800, DisplayPolicy.Always);

        Assert.True(d.Show);
        Assert.Equal(InputMode.English, d.Mode);
    }
}
