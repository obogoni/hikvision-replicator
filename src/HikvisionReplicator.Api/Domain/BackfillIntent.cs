using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

/// <summary>
/// Records that a newly registered reader is owed the whole active roster. One row instead
/// of 50,000, so registering hardware on match day returns immediately; the rows themselves
/// are staged later by the expansion. The clock is always passed in (AD-023).
/// </summary>
public class BackfillIntent : AggregateRoot, IAggregateRoot
{
    public int Id { get; private set; }
    public int DeviceId { get; private set; }
    public BackfillStatus Status { get; private set; }
    public DateTime? ExpandedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private BackfillIntent() { } // for EF Core

    private BackfillIntent(int deviceId, DateTime now)
    {
        DeviceId = deviceId;
        Status = BackfillStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static BackfillIntent Create(int deviceId, DateTime now) => new(deviceId, now);

    /// <summary>
    /// The roster has been staged: terminal. A second call is refused, which is what stops
    /// a repeated expansion from queueing the fleet twice (REP-20).
    /// </summary>
    public OneOf<Success, ValidationError> MarkExpanded(DateTime now)
    {
        if (Status != BackfillStatus.Pending)
            return new ValidationError(Errors.StatusField, Errors.AlreadyExpanded);

        Status = BackfillStatus.Expanded;
        ExpandedAt = now;
        UpdatedAt = now;

        return new Success();
    }

    public static class Errors
    {
        public const string StatusField = "status";
        public const string AlreadyExpanded = "This device's backfill has already been expanded.";
    }
}
