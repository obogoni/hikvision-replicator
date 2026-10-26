using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure.Worker;

namespace HikvisionReplicator.Tests.Infrastructure.Worker;

/// <summary>
/// The recorder rules of the design, one row at a time: what an attempt's outcome does to its
/// replication (WRK-11, WRK-16, WRK-17, WRK-20) and when no call is made at all (WRK-21). The
/// schedule carries the spec's figures — 2 s doubling to 60 s, five attempts.
/// </summary>
public class OutcomeDecisionTests
{
    private static readonly DateTime Now = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    private static readonly RetrySchedule Schedule = new(
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(60),
        maxAttempts: 5
    );

    private static ReplicationAction After(
        AttemptOutcome outcome,
        int attemptCountBeforeCall = 0,
        bool newerPendingExists = false
    ) =>
        OutcomeDecision.AfterCall(outcome, attemptCountBeforeCall, newerPendingExists, Schedule, Now);

    // ─── WRK-03: the reader accepted it ───

    [Fact]
    public void A_delivered_record_succeeds()
    {
        Assert.IsType<ReplicationAction.Succeed>(After(AttemptOutcome.Delivered));
    }

    [Fact]
    public void A_delivered_record_succeeds_even_when_a_newer_intent_is_waiting_behind_it()
    {
        Assert.IsType<ReplicationAction.Succeed>(
            After(AttemptOutcome.Delivered, newerPendingExists: true)
        );
    }

    // ─── WRK-11 / WRK-14: an unreachable reader costs the record nothing ───

    [Fact]
    public void A_record_whose_reader_was_unreachable_goes_back_without_an_attempt()
    {
        Assert.IsType<ReplicationAction.Release>(After(AttemptOutcome.Unreachable));
    }

    [Fact]
    public void An_unreachable_reader_never_dead_letters_a_record_however_many_rejections_it_had()
    {
        Assert.IsType<ReplicationAction.Release>(
            After(AttemptOutcome.Unreachable, attemptCountBeforeCall: 4)
        );
    }

    // ─── WRK-16: a rejection below the maximum waits 2 s × 2^(attempts − 1) ───

    [Fact]
    public void A_first_rejection_is_retried_after_the_first_backoff()
    {
        var action = After(AttemptOutcome.Rejected, attemptCountBeforeCall: 0);

        var retry = Assert.IsType<ReplicationAction.RetryAt>(action);
        Assert.Equal(Now.AddSeconds(2), retry.NotBefore);
    }

    [Fact]
    public void A_fourth_rejection_is_still_retried_after_a_longer_backoff()
    {
        var action = After(AttemptOutcome.Rejected, attemptCountBeforeCall: 3);

        var retry = Assert.IsType<ReplicationAction.RetryAt>(action);
        Assert.Equal(Now.AddSeconds(16), retry.NotBefore);
    }

    // ─── WRK-17: the rejection that reaches the maximum is the dead letter ───

    [Fact]
    public void A_fifth_rejection_dead_letters_the_record()
    {
        Assert.IsType<ReplicationAction.DeadLetter>(
            After(AttemptOutcome.Rejected, attemptCountBeforeCall: 4)
        );
    }

    // ─── WRK-20: an attempt that did not land, overtaken by a newer intent, ends there ───

    [Fact]
    public void An_unreachable_attempt_overtaken_by_a_newer_intent_is_superseded_not_released()
    {
        Assert.IsType<ReplicationAction.SupersedeAfterAttempt>(
            After(AttemptOutcome.Unreachable, newerPendingExists: true)
        );
    }

    [Fact]
    public void A_rejected_attempt_overtaken_by_a_newer_intent_is_superseded_not_retried()
    {
        Assert.IsType<ReplicationAction.SupersedeAfterAttempt>(
            After(AttemptOutcome.Rejected, attemptCountBeforeCall: 1, newerPendingExists: true)
        );
    }

    [Fact]
    public void A_final_rejection_overtaken_by_a_newer_intent_is_superseded_not_dead_lettered()
    {
        Assert.IsType<ReplicationAction.SupersedeAfterAttempt>(
            After(AttemptOutcome.Rejected, attemptCountBeforeCall: 4, newerPendingExists: true)
        );
    }

    // ─── WRK-21: no face, no call — but a removal is exactly what a tombstone asks for ───

    [Theory]
    [InlineData(ReplicationOperation.Add)]
    [InlineData(ReplicationOperation.Update)]
    public void Enrolling_a_tombstoned_spectator_ends_without_a_call(ReplicationOperation operation)
    {
        Assert.IsType<ReplicationAction.SupersedeWithoutCall>(
            OutcomeDecision.BeforeCall(operation, userTombstoned: true)
        );
    }

    [Fact]
    public void Removing_a_tombstoned_spectator_goes_ahead()
    {
        Assert.Null(OutcomeDecision.BeforeCall(ReplicationOperation.Remove, userTombstoned: true));
    }

    [Theory]
    [InlineData(ReplicationOperation.Add)]
    [InlineData(ReplicationOperation.Update)]
    [InlineData(ReplicationOperation.Remove)]
    public void Work_for_an_active_spectator_goes_ahead(ReplicationOperation operation)
    {
        Assert.Null(OutcomeDecision.BeforeCall(operation, userTombstoned: false));
    }
}
