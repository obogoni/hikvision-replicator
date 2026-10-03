using HikvisionReplicator.Api.Shared;
using OneOf;

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

    /// <summary>Takes the work in hand: <c>Pending → InProgress</c>.</summary>
    public OneOf<Success, ValidationError> Begin(DateTime now) =>
        MoveTo(ReplicationStatus.Pending, ReplicationStatus.InProgress, Errors.CannotBegin, now);

    /// <summary>The device accepted it: <c>InProgress → Succeeded</c>, terminal.</summary>
    public OneOf<Success, ValidationError> Succeed(DateTime now) =>
        MoveTo(ReplicationStatus.InProgress, ReplicationStatus.Succeeded, Errors.CannotSucceed, now);

    /// <summary>
    /// The attempt cost something and did not land: <c>InProgress → Failed</c>, one more
    /// attempt on the count (REP-30) and the device's complaint kept, truncated (REP-33).
    /// </summary>
    public OneOf<Success, ValidationError> Fail(string error, DateTime now)
    {
        if (Status != ReplicationStatus.InProgress)
            return new ValidationError(Errors.StatusField, Errors.CannotFail);

        Status = ReplicationStatus.Failed;
        AttemptCount++;
        LastError = error.Length > MaxLastErrorLength ? error[..MaxLastErrorLength] : error;
        UpdatedAt = now;

        return new Success();
    }

    /// <summary>
    /// Back into the queue: <c>Failed → Pending</c>. The attempt count survives, because it
    /// is what feature 4's backoff will read (REP-31).
    /// </summary>
    public OneOf<Success, ValidationError> Retry(DateTime now) =>
        MoveTo(ReplicationStatus.Failed, ReplicationStatus.Pending, Errors.CannotRetry, now);

    /// <summary>
    /// A newer intent replaced this one: <c>Pending → Superseded</c>, terminal. In-flight
    /// work is never superseded — it is left to reach its own terminal status (REP-12).
    /// </summary>
    public OneOf<Success, ValidationError> Supersede(DateTime now) =>
        MoveTo(ReplicationStatus.Pending, ReplicationStatus.Superseded, Errors.CannotSupersede, now);

    // The guard returns before any assignment, so a refused transition leaves the aggregate
    // exactly as it was (REP-28). Succeeded and Superseded are the `from` of nothing, which
    // is what makes them terminal (REP-29).
    private OneOf<Success, ValidationError> MoveTo(
        ReplicationStatus from,
        ReplicationStatus to,
        string refusal,
        DateTime now
    )
    {
        if (Status != from)
            return new ValidationError(Errors.StatusField, refusal);

        Status = to;
        UpdatedAt = now;

        return new Success();
    }

    public static class Errors
    {
        public const string StatusField = "status";
        public const string CannotBegin = "Only pending work can be started.";
        public const string CannotSucceed = "Only work in progress can succeed.";
        public const string CannotFail = "Only work in progress can fail.";
        public const string CannotRetry = "Only failed work can be retried.";
        public const string CannotSupersede = "Only pending work can be superseded.";
    }
}
