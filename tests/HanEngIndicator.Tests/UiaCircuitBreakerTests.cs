using HanEngIndicator.Services;
using Xunit;

namespace HanEngIndicator.Tests;

public class UiaCircuitBreakerTests
{
    private static readonly DateTime T0 = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(30);

    [Fact]
    public void Starts_closed()
    {
        var b = new UiaCircuitBreaker(3, Cooldown);
        Assert.False(b.IsOpen(T0));
    }

    [Fact]
    public void Trips_open_after_consecutive_unhealthy_results()
    {
        var b = new UiaCircuitBreaker(3, Cooldown);
        b.Record(healthy: false, T0);
        b.Record(healthy: false, T0);
        Assert.False(b.IsOpen(T0)); // 2 < limit

        b.Record(healthy: false, T0); // 3rd trip
        Assert.True(b.IsOpen(T0));
    }

    [Fact]
    public void Cooldown_is_measured_from_the_trip_time_not_call_start()
    {
        var b = new UiaCircuitBreaker(1, Cooldown);
        DateTime tripTime = T0.AddMinutes(5);

        b.Record(healthy: false, tripTime);

        Assert.True(b.IsOpen(tripTime));
        Assert.True(b.IsOpen(tripTime + TimeSpan.FromSeconds(29)));
        Assert.False(b.IsOpen(tripTime + TimeSpan.FromSeconds(31)));
    }

    [Fact]
    public void A_healthy_result_resets_the_streak()
    {
        var b = new UiaCircuitBreaker(3, Cooldown);
        b.Record(healthy: false, T0);
        b.Record(healthy: false, T0);
        b.Record(healthy: true, T0);  // reset
        b.Record(healthy: false, T0);
        b.Record(healthy: false, T0);

        Assert.False(b.IsOpen(T0)); // only 2 in the current streak
    }

    [Fact]
    public void Reopens_closed_after_cooldown_expires()
    {
        var b = new UiaCircuitBreaker(1, Cooldown);
        b.Record(healthy: false, T0);
        Assert.True(b.IsOpen(T0));

        DateTime after = T0 + Cooldown + TimeSpan.FromSeconds(1);
        Assert.False(b.IsOpen(after));
    }
}
