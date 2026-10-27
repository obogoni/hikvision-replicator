using HikvisionReplicator.Api.Infrastructure.Worker;

namespace HikvisionReplicator.Tests.Infrastructure.Worker;

/// <summary>
/// WRK-16's backoff and WRK-17's dead-letter threshold. The defaults are the spec's own figures —
/// 2 s doubling to a 60 s ceiling, five attempts — written out here rather than read from the
/// options class, so a changed default fails a test instead of moving with it.
/// </summary>
public class RetryScheduleTests
{
    private static readonly RetrySchedule SpecDefaults = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(60),
        maxAttempts: 5
    );

    // ─── WRK-16: 2 s × 2^(attempts − 1), capped at 60 s ───

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 4)]
    [InlineData(3, 8)]
    [InlineData(4, 16)]
    public void Each_rejection_doubles_the_wait_before_the_next_attempt(int attempts, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), SpecDefaults.DelayAfter(attempts));
    }

    [Fact]
    public void The_wait_keeps_doubling_while_it_is_below_the_ceiling()
    {
        Assert.Equal(TimeSpan.FromSeconds(32), SpecDefaults.DelayAfter(5));
    }

    [Fact]
    public void The_wait_stops_at_the_ceiling_once_doubling_would_pass_it()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), SpecDefaults.DelayAfter(6));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1_000)]
    [InlineData(int.MaxValue)]
    public void The_wait_holds_at_the_ceiling_however_many_attempts_have_been_made(int attempts)
    {
        Assert.Equal(TimeSpan.FromSeconds(60), SpecDefaults.DelayAfter(attempts));
    }

    [Fact]
    public void A_configured_base_and_ceiling_replace_the_defaults()
    {
        var schedule = new RetrySchedule(
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(500),
            maxAttempts: 5
        );

        Assert.Equal(TimeSpan.FromMilliseconds(100), schedule.DelayAfter(1));
        Assert.Equal(TimeSpan.FromMilliseconds(400), schedule.DelayAfter(3));
        Assert.Equal(TimeSpan.FromMilliseconds(500), schedule.DelayAfter(4));
    }

    [Fact]
    public void There_is_no_wait_to_compute_before_any_rejection_has_been_counted()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SpecDefaults.DelayAfter(0));
    }

    // ─── WRK-17: the dead letter is reached at the maximum, not one after it ───

    [Fact]
    public void A_record_below_the_maximum_is_still_retried()
    {
        Assert.False(SpecDefaults.IsExhausted(4));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(6)]
    public void A_record_at_the_maximum_is_dead_lettered(int attempts)
    {
        Assert.True(SpecDefaults.IsExhausted(attempts));
    }

    [Fact]
    public void A_configured_maximum_replaces_the_default()
    {
        var schedule = new RetrySchedule(
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(60),
            maxAttempts: 2
        );

        Assert.False(schedule.IsExhausted(1));
        Assert.True(schedule.IsExhausted(2));
    }
}
