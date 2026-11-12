namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// How long a rejected record waits before it is offered to its reader again, and when it stops
/// being offered at all (WRK-16, WRK-17). Only a rejection reaches this: an unreachable reader
/// costs an item nothing (WRK-11), so the attempt count read here is a count of refusals.
/// <para>
/// Pure and clock-free — the caller adds the delay to its own <c>now</c> (AD-023) and stores the
/// result on the row, so a backoff survives a restart instead of living in worker memory.
/// </para>
/// </summary>
public sealed class RetrySchedule(TimeSpan baseDelay, TimeSpan maxDelay, int maxAttempts)
{
    /// <summary>
    /// <c>baseDelay × 2^(attempts − 1)</c>, capped at <c>maxDelay</c>. <paramref name="attempts"/>
    /// is the count <em>after</em> the rejection was recorded, so the first rejection waits
    /// exactly <c>baseDelay</c>. Computed in floating point so a large count saturates at the
    /// cap rather than overflowing the tick count.
    /// </summary>
    public TimeSpan DelayAfter(int attempts)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(attempts, 1);

        var ticks = baseDelay.Ticks * Math.Pow(2, attempts - 1);

        return ticks >= maxDelay.Ticks ? maxDelay : TimeSpan.FromTicks((long)ticks);
    }

    /// <summary>
    /// The dead-letter threshold: once the count reaches <c>maxAttempts</c> the record stays
    /// <c>Failed</c> and is never taken again (WRK-17).
    /// </summary>
    public bool IsExhausted(int attempts) => attempts >= maxAttempts;
}
