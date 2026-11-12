using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// How a device call ended, as the decision needs it. A call that timed out is
/// <see cref="Unreachable"/> (WRK-11); an adapter that threw is <see cref="Rejected"/>, so a bug
/// surfaces as dead letters rather than an endless pause.
/// </summary>
public enum AttemptOutcome
{
    Delivered,
    Rejected,
    Unreachable,
}

/// <summary>What an attempt does to its replication — exactly one of these, saved once.</summary>
public abstract record ReplicationAction
{
    private ReplicationAction() { }

    /// <summary>The reader accepted it: <c>Succeed</c> (WRK-03).</summary>
    public sealed record Succeed : ReplicationAction;

    /// <summary>The reader never judged it: <c>Release</c>, no attempt counted (WRK-11).</summary>
    public sealed record Release : ReplicationAction;

    /// <summary>The reader refused it: <c>Fail</c>, then <c>Retry</c> not before <paramref name="NotBefore"/> (WRK-16).</summary>
    public sealed record RetryAt(DateTime NotBefore) : ReplicationAction;

    /// <summary>The reader refused it for the last time: <c>Fail</c> only — the dead letter (WRK-17).</summary>
    public sealed record DeadLetter : ReplicationAction;

    /// <summary>A newer intent is already queued for the pair: end this one there (WRK-20).</summary>
    public sealed record SupersedeAfterAttempt : ReplicationAction;

    /// <summary>The spectator is gone and so is the face: no call is made at all (WRK-21).</summary>
    public sealed record SupersedeWithoutCall : ReplicationAction;
}

/// <summary>
/// The worker's recorder rules as a pure function, so every branch is provable without a
/// database or a reader. It decides; it does not touch the aggregate.
/// </summary>
// SPEC_DEVIATION: two functions rather than the one tasks.md T7 describes.
// Reason: the tombstone rule (WRK-21) must be decided before a call exists and every other
// rule after one, so a single function would need an outcome for a call that was never made.
public static class OutcomeDecision
{
    /// <summary>
    /// Whether a call should be made at all. An <c>Add</c> or <c>Update</c> for a tombstoned
    /// spectator has no face left to send (USR-32) and a <c>Remove</c> is already queued behind
    /// it (REP-03), so it ends without a call. A <c>Remove</c> proceeds — taking them off the
    /// reader is exactly what a tombstone asks for. <c>null</c> means proceed.
    /// </summary>
    public static ReplicationAction? BeforeCall(ReplicationOperation operation, bool userTombstoned) =>
        userTombstoned && operation is ReplicationOperation.Add or ReplicationOperation.Update
            ? new ReplicationAction.SupersedeWithoutCall()
            : null;

    /// <summary>
    /// What the call's outcome does to the replication. <paramref name="attemptCountBeforeCall"/>
    /// is the row's count as it was taken in hand, so a rejection now is attempt
    /// <c>attemptCountBeforeCall + 1</c> — the count WRK-16 and WRK-17 are written against.
    /// </summary>
    public static ReplicationAction AfterCall(
        AttemptOutcome outcome,
        int attemptCountBeforeCall,
        bool newerPendingExists,
        RetrySchedule schedule,
        DateTime now
    )
    {
        ArgumentNullException.ThrowIfNull(schedule);

        // A delivered record is delivered: the newer intent is delivered after it (WRK-08), and
        // superseding a success would misreport what the reader holds.
        if (outcome == AttemptOutcome.Delivered)
            return new ReplicationAction.Succeed();

        // Any other ending with newer intent queued must not return to Pending beside it, and
        // a dead letter for intent already replaced would set aside nothing anyone still wants.
        if (newerPendingExists)
            return new ReplicationAction.SupersedeAfterAttempt();

        if (outcome == AttemptOutcome.Unreachable)
            return new ReplicationAction.Release();

        var attempts = attemptCountBeforeCall + 1;

        return schedule.IsExhausted(attempts)
            ? new ReplicationAction.DeadLetter()
            : new ReplicationAction.RetryAt(now + schedule.DelayAfter(attempts));
    }
}
