namespace HanEngIndicator.Services;

/// <summary>
/// Pure circuit breaker for the UI Automation caret path. After a number of
/// consecutive "unhealthy" attempts (a UIA call that did not return within the
/// worker's wait budget) it opens for a cooldown, during which UIA is skipped
/// entirely and the caller falls back to the mouse. A single healthy (fast)
/// attempt closes it again.
///
/// No Win32/WinForms dependency, so it is unit-tested on any OS.
/// </summary>
public sealed class UiaCircuitBreaker
{
    private readonly int _slowStreakLimit;
    private readonly TimeSpan _cooldown;

    private int _slowStreak;
    private DateTime _openUntilUtc = DateTime.MinValue;

    public UiaCircuitBreaker(int slowStreakLimit, TimeSpan cooldown)
    {
        _slowStreakLimit = Math.Max(1, slowStreakLimit);
        _cooldown = cooldown;
    }

    /// <summary>True while the breaker is open (UIA should be skipped).</summary>
    public bool IsOpen(DateTime nowUtc) => nowUtc < _openUntilUtc;

    /// <summary>
    /// Record the outcome of a UIA attempt.
    /// <paramref name="healthy"/> = the call returned within budget.
    /// A run of unhealthy results trips the breaker; the cooldown is measured
    /// from the moment it trips (<paramref name="nowUtc"/>), never from a stale
    /// call-start time.
    /// </summary>
    public void Record(bool healthy, DateTime nowUtc)
    {
        if (healthy)
        {
            _slowStreak = 0;
            return;
        }

        if (++_slowStreak >= _slowStreakLimit)
        {
            _openUntilUtc = nowUtc + _cooldown;
            _slowStreak = 0;
        }
    }
}
