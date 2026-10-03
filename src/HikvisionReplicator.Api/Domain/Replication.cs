using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain;

/// <summary>
/// One unit of intent: put this spectator on this device, or take them off it. The clock is
/// always passed in, never read (AD-023). Nothing here executes anything — draining is
/// feature 4's, and the status model exists so that feature adds a runner and not domain
/// rules (AD-039).
/// </summary>
public class Replication : AggregateRoot, IAggregateRoot
{
    /// <summary>
    /// A device error string is written by hardware this service does not control, so the
    /// column it lands in is bounded here rather than hoped to be short (REP-33).
    /// </summary>
    public const int MaxLastErrorLength = 1000;

    public int Id { get; private set; }
    public int UserId { get; private set; }
    public int DeviceId { get; private set; }
    public ReplicationOperation Operation { get; private set; }
    public ReplicationLane Lane { get; private set; }
    public ReplicationStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Replication() { } // for EF Core

    private Replication(
        int userId,
        int deviceId,
        ReplicationOperation operation,
        ReplicationLane lane,
        DateTime now
    )
    {
        UserId = userId;
        DeviceId = deviceId;
        Operation = operation;
        Lane = lane;
        Status = ReplicationStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Queues the intent. Nothing is validated because nothing can be invalid: the pair is
    /// two foreign keys and the operation and lane are closed sets.
    /// </summary>
    public static Replication Create(
        int userId,
        int deviceId,
        ReplicationOperation operation,
        ReplicationLane lane,
        DateTime now
    ) => new(userId, deviceId, operation, lane, now);
}
