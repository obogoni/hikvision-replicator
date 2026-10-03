using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class ReplicationTransitionTests
{
    private static readonly DateTime QueuedOn = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StartedOn = new(2026, 10, 3, 18, 0, 5, DateTimeKind.Utc);
    private static readonly DateTime SettledOn = new(2026, 10, 3, 18, 0, 9, DateTimeKind.Utc);
    private static readonly DateTime RefusedOn = new(2026, 10, 3, 19, 0, 0, DateTimeKind.Utc);

    private sealed record Snapshot(
        ReplicationStatus Status,
        int AttemptCount,
        string? LastError,
        DateTime UpdatedAt
    );

    private static Snapshot Capture(Replication work) =>
        new(work.Status, work.AttemptCount, work.LastError, work.UpdatedAt);

    private static Replication Queued() =>
        Replication.Create(7, 42, ReplicationOperation.Add, ReplicationLane.Live, QueuedOn);

    private static Replication InStatus(ReplicationStatus status)
    {
        var work = Queued();

        switch (status)
        {
            case ReplicationStatus.Pending:
                break;
            case ReplicationStatus.InProgress:
                work.Begin(StartedOn);
                break;
            case ReplicationStatus.Succeeded:
                work.Begin(StartedOn);
                work.Succeed(SettledOn);
                break;
            case ReplicationStatus.Failed:
                work.Begin(StartedOn);
                work.Fail("device said no", SettledOn);
                break;
            case ReplicationStatus.Superseded:
                work.Supersede(SettledOn);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(status));
        }

        return work;
    }

    // ─── REP-27 / REP-32: the five legal transitions, each timestamped from the clock ───

    [Fact]
    public void Pending_work_can_be_taken_in_hand()
    {
        var work = InStatus(ReplicationStatus.Pending);

        var result = work.Begin(StartedOn);

        Assert.True(result.IsT0);
        Assert.Equal(ReplicationStatus.InProgress, work.Status);
        Assert.Equal(StartedOn, work.UpdatedAt);
        Assert.Equal(QueuedOn, work.CreatedAt);
    }

    [Fact]
    public void Work_in_hand_can_succeed()
    {
        var work = InStatus(ReplicationStatus.InProgress);

        var result = work.Succeed(SettledOn);

        Assert.True(result.IsT0);
        Assert.Equal(ReplicationStatus.Succeeded, work.Status);
        Assert.Equal(SettledOn, work.UpdatedAt);
    }

    [Fact]
    public void Work_in_hand_can_fail()
    {
        var work = InStatus(ReplicationStatus.InProgress);

        var result = work.Fail("device said no", SettledOn);

        Assert.True(result.IsT0);
        Assert.Equal(ReplicationStatus.Failed, work.Status);
        Assert.Equal(SettledOn, work.UpdatedAt);
    }

    [Fact]
    public void Failed_work_can_go_back_into_the_queue()
    {
        var work = InStatus(ReplicationStatus.Failed);

        var result = work.Retry(RefusedOn);

        Assert.True(result.IsT0);
        Assert.Equal(ReplicationStatus.Pending, work.Status);
        Assert.Equal(RefusedOn, work.UpdatedAt);
    }

    [Fact]
    public void Pending_work_can_be_replaced_by_a_newer_intent()
    {
        var work = InStatus(ReplicationStatus.Pending);

        var result = work.Supersede(SettledOn);

        Assert.True(result.IsT0);
        Assert.Equal(ReplicationStatus.Superseded, work.Status);
        Assert.Equal(SettledOn, work.UpdatedAt);
    }

    // ─── REP-28 / REP-29: every other transition is refused and changes nothing. The
    //     Succeeded and Superseded rows are what makes those two terminal.             ───

    [Theory]
    [InlineData(ReplicationStatus.InProgress)]
    [InlineData(ReplicationStatus.Succeeded)]
    [InlineData(ReplicationStatus.Failed)]
    [InlineData(ReplicationStatus.Superseded)]
    public void Only_pending_work_can_be_taken_in_hand(ReplicationStatus status)
    {
        var work = InStatus(status);
        var before = Capture(work);

        var result = work.Begin(RefusedOn);

        Assert.True(result.IsT1);
        Assert.Equal(Replication.Errors.StatusField, result.AsT1.Field);
        Assert.Equal(Replication.Errors.CannotBegin, result.AsT1.Message);
        Assert.Equal(before, Capture(work));
    }

    [Theory]
    [InlineData(ReplicationStatus.Pending)]
    [InlineData(ReplicationStatus.Succeeded)]
    [InlineData(ReplicationStatus.Failed)]
    [InlineData(ReplicationStatus.Superseded)]
    public void Only_work_in_hand_can_succeed(ReplicationStatus status)
    {
        var work = InStatus(status);
        var before = Capture(work);

        var result = work.Succeed(RefusedOn);

        Assert.True(result.IsT1);
        Assert.Equal(Replication.Errors.StatusField, result.AsT1.Field);
        Assert.Equal(Replication.Errors.CannotSucceed, result.AsT1.Message);
        Assert.Equal(before, Capture(work));
    }

    [Theory]
    [InlineData(ReplicationStatus.Pending)]
    [InlineData(ReplicationStatus.Succeeded)]
    [InlineData(ReplicationStatus.Failed)]
    [InlineData(ReplicationStatus.Superseded)]
    public void Only_work_in_hand_can_fail(ReplicationStatus status)
    {
        var work = InStatus(status);
        var before = Capture(work);

        var result = work.Fail("a later complaint", RefusedOn);

        Assert.True(result.IsT1);
        Assert.Equal(Replication.Errors.StatusField, result.AsT1.Field);
        Assert.Equal(Replication.Errors.CannotFail, result.AsT1.Message);
        Assert.Equal(before, Capture(work));
    }

    [Theory]
    [InlineData(ReplicationStatus.Pending)]
    [InlineData(ReplicationStatus.InProgress)]
    [InlineData(ReplicationStatus.Succeeded)]
    [InlineData(ReplicationStatus.Superseded)]
    public void Only_failed_work_can_go_back_into_the_queue(ReplicationStatus status)
    {
        var work = InStatus(status);
        var before = Capture(work);

        var result = work.Retry(RefusedOn);

        Assert.True(result.IsT1);
        Assert.Equal(Replication.Errors.StatusField, result.AsT1.Field);
        Assert.Equal(Replication.Errors.CannotRetry, result.AsT1.Message);
        Assert.Equal(before, Capture(work));
    }

    [Theory]
    [InlineData(ReplicationStatus.InProgress)]
    [InlineData(ReplicationStatus.Succeeded)]
    [InlineData(ReplicationStatus.Failed)]
    [InlineData(ReplicationStatus.Superseded)]
    public void Only_pending_work_can_be_replaced_by_a_newer_intent(ReplicationStatus status)
    {
        var work = InStatus(status);
        var before = Capture(work);

        var result = work.Supersede(RefusedOn);

        Assert.True(result.IsT1);
        Assert.Equal(Replication.Errors.StatusField, result.AsT1.Field);
        Assert.Equal(Replication.Errors.CannotSupersede, result.AsT1.Message);
        Assert.Equal(before, Capture(work));
    }

    // ─── REP-30: a failure costs exactly one attempt and keeps what the device said ───

    [Fact]
    public void A_failed_attempt_is_counted_and_what_went_wrong_is_kept()
    {
        var work = InStatus(ReplicationStatus.InProgress);

        work.Fail("device said no", SettledOn);

        Assert.Equal(1, work.AttemptCount);
        Assert.Equal("device said no", work.LastError);
    }

    [Fact]
    public void Each_failed_attempt_adds_exactly_one_to_the_count()
    {
        var work = InStatus(ReplicationStatus.Failed);
        work.Retry(RefusedOn);
        work.Begin(RefusedOn);

        work.Fail("device said no again", RefusedOn);

        Assert.Equal(2, work.AttemptCount);
        Assert.Equal("device said no again", work.LastError);
    }

    // ─── REP-31: a retry does not forgive the attempts already spent ───

    [Fact]
    public void Requeued_work_keeps_the_attempts_it_has_already_cost()
    {
        var work = InStatus(ReplicationStatus.Failed);

        work.Retry(RefusedOn);

        Assert.Equal(1, work.AttemptCount);
    }

    // ─── REP-33: an unbounded device string is stored bounded, and still lands ───

    [Fact]
    public void An_overlong_device_complaint_is_stored_truncated_and_the_failure_still_lands()
    {
        var work = InStatus(ReplicationStatus.InProgress);
        var complaint = new string('x', 1001);

        var result = work.Fail(complaint, SettledOn);

        Assert.True(result.IsT0);
        Assert.Equal(ReplicationStatus.Failed, work.Status);
        Assert.NotNull(work.LastError);
        Assert.Equal(1000, work.LastError.Length);
        Assert.Equal(complaint[..1000], work.LastError);
    }

    [Fact]
    public void A_device_complaint_at_the_limit_is_stored_whole()
    {
        var work = InStatus(ReplicationStatus.InProgress);
        var complaint = new string('x', 1000);

        work.Fail(complaint, SettledOn);

        Assert.Equal(complaint, work.LastError);
    }
}
