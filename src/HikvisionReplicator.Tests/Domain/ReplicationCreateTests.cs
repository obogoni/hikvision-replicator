using System.Reflection;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class ReplicationCreateTests
{
    private static readonly DateTime QueuedOn = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);

    private static Replication Queued(
        ReplicationOperation operation = ReplicationOperation.Add,
        ReplicationLane lane = ReplicationLane.Live
    ) => Replication.Create(7, 42, operation, lane, QueuedOn);

    // ─── REP-26: a newly queued intent has been attempted by nobody ───

    [Fact]
    public void Newly_queued_work_is_pending()
    {
        Assert.Equal(ReplicationStatus.Pending, Queued().Status);
    }

    [Fact]
    public void Newly_queued_work_has_been_attempted_no_times()
    {
        Assert.Equal(0, Queued().AttemptCount);
    }

    [Fact]
    public void Newly_queued_work_carries_no_error()
    {
        Assert.Null(Queued().LastError);
    }

    // ─── The intent records which spectator goes to which reader, and why ───

    [Fact]
    public void Queued_work_records_the_spectator_and_the_reader_it_is_owed_to()
    {
        var replication = Queued();

        Assert.Equal(7, replication.UserId);
        Assert.Equal(42, replication.DeviceId);
    }

    [Theory]
    [InlineData(ReplicationOperation.Add, ReplicationLane.Live)]
    [InlineData(ReplicationOperation.Update, ReplicationLane.Live)]
    [InlineData(ReplicationOperation.Remove, ReplicationLane.Live)]
    [InlineData(ReplicationOperation.Add, ReplicationLane.Bulk)]
    public void Queued_work_carries_the_operation_and_lane_it_was_queued_for(
        ReplicationOperation operation,
        ReplicationLane lane
    )
    {
        var replication = Queued(operation, lane);

        Assert.Equal(operation, replication.Operation);
        Assert.Equal(lane, replication.Lane);
    }

    // ─── AD-023: the clock is supplied, never read ───

    [Fact]
    public void Queued_work_is_timestamped_from_the_supplied_clock()
    {
        var replication = Queued();

        Assert.Equal(QueuedOn, replication.CreatedAt);
        Assert.Equal(QueuedOn, replication.UpdatedAt);
    }

    // ─── AD-005: the factory is the only way in ───

    [Fact]
    public void Queued_work_cannot_be_constructed_around_the_factory()
    {
        var reachable = typeof(Replication).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public
        );

        Assert.Empty(reachable);
    }

    // ─── REP-33: the error column is bounded ───

    [Fact]
    public void A_stored_error_is_bounded_to_a_thousand_characters()
    {
        Assert.Equal(1000, Replication.MaxLastErrorLength);
    }

    // ─── Work staged for a spectator the database has not keyed yet ───

    [Fact]
    public void Work_staged_for_an_unsaved_spectator_carries_the_spectator_itself()
    {
        var user = User
            .Create(
                "TICKET-1",
                "Ada Lovelace",
                "004215",
                FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
                [0x01, 0x02, 0x03],
                QueuedOn
            )
            .AsT0;

        var replication = Replication.Create(
            user,
            42,
            ReplicationOperation.Add,
            ReplicationLane.Live,
            QueuedOn
        );

        Assert.Same(user, replication.User);
        Assert.Equal(42, replication.DeviceId);
        Assert.Equal(ReplicationStatus.Pending, replication.Status);
    }

    /// <summary>
    /// The int-based factory stays, because the expansion and the live fan-out across
    /// already-registered readers both know the key and have no aggregate to hand.
    /// </summary>
    [Fact]
    public void Work_staged_for_a_known_spectator_carries_only_its_key()
    {
        var replication = Queued();

        Assert.Equal(7, replication.UserId);
        Assert.Null(replication.User);
    }

    /// <summary>
    /// A replication is only ever staged against a reader already in the catalogue, so the
    /// unsaved-aggregate problem does not exist on that side and no navigation is carried.
    /// </summary>
    [Fact]
    public void Queued_work_carries_no_reader_navigation()
    {
        Assert.Null(typeof(Replication).GetProperty(nameof(Device)));
    }
}
