namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>What a recorded call did to a reader's pause, when it did anything (WRK-29).</summary>
public enum PauseTransition
{
    Paused,
    Resumed,
}

/// <summary>
/// One reader's consecutive-failure breaker (WRK-12, WRK-13, WRK-15). Our own rather than
/// Polly's: a serial device loop needs "the last N calls were unreachable", and Polly v8's
/// breaker is ratio-over-a-window only (AD-043).
/// <para>
/// Only <c>Unreachable</c> counts toward a pause. A reader that answered — even to refuse the
/// record — is up, so an answer of either kind resets the count and ends a pause (WRK-15). Held
/// in the loop's memory and per worker instance; persisted reachability is feature 6's. Time is
/// passed in (AD-023).
/// </para>
/// </summary>
public sealed class DevicePause(
    int pauseAfterConsecutiveUnreachable,
    TimeSpan probeInitialDelay,
    TimeSpan probeMaxDelay
)
{
    private int _consecutiveUnreachable;
    private int _failedProbes;

    /// <summary>When a paused reader may next be called with its head item; <c>null</c> when not paused.</summary>
    public DateTime? ProbeDueAt { get; private set; }

    /// <summary>Not paused, or paused with its probe due (WRK-12, WRK-13).</summary>
    public bool CanAttempt(DateTime now) => ProbeDueAt is not { } due || now >= due;

    /// <summary>
    /// A call the reader never answered. Trips the pause on the
    /// <c>pauseAfterConsecutiveUnreachable</c>-th in a row; while paused, it is a failed probe and
    /// doubles the wait before the next one, up to the ceiling.
    /// </summary>
    public PauseTransition? RecordUnreachable(DateTime now)
    {
        if (ProbeDueAt is not null)
        {
            _failedProbes++;
            ProbeDueAt = now + ProbeDelay();
            return null;
        }

        _consecutiveUnreachable++;
        if (_consecutiveUnreachable < pauseAfterConsecutiveUnreachable)
            return null;

        _failedProbes = 0;
        ProbeDueAt = now + ProbeDelay();
        return PauseTransition.Paused;
    }

    /// <summary>
    /// The reader answered — delivered or rejected, either proves it is reachable. Resets the
    /// count, and reports <see cref="PauseTransition.Resumed"/> only if it ends a pause, so a
    /// resume is announced once.
    /// </summary>
    public PauseTransition? RecordAnswered()
    {
        _consecutiveUnreachable = 0;

        if (ProbeDueAt is null)
            return null;

        ProbeDueAt = null;
        _failedProbes = 0;
        return PauseTransition.Resumed;
    }

    // Floating point so an outage of any length saturates at the ceiling rather than
    // overflowing the tick count — WRK-14 puts no bound on how many probes can fail.
    private TimeSpan ProbeDelay()
    {
        var ticks = probeInitialDelay.Ticks * Math.Pow(2, _failedProbes);

        return ticks >= probeMaxDelay.Ticks ? probeMaxDelay : TimeSpan.FromTicks((long)ticks);
    }
}
