using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public class Replication : IAggregateRoot
{
    public int Id { get; private set; }
    public int UserId { get; private set; }
    public int DeviceId { get; private set; }
    public ReplicationType Type { get; private set; }
    public ReplicationStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Replication() { } // for EF Core

    private Replication(int userId, int deviceId, ReplicationType type, DateTime now)
    {
        UserId = userId;
        DeviceId = deviceId;
        Type = type;
        Status = ReplicationStatus.Pending;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static OneOf<Replication, ValidationError> Create(
        int? userId,
        int? deviceId,
        ReplicationType? type,
        DateTime now)
    {
        if (userId is null or <= 0)
            return new ValidationError(Errors.UserIdField, Errors.UserIdRequired);

        if (deviceId is null or <= 0)
            return new ValidationError(Errors.DeviceIdField, Errors.DeviceIdRequired);

        if (type is null)
            return new ValidationError(Errors.TypeField, Errors.TypeRequired);

        return new Replication(userId.Value, deviceId.Value, type.Value, now);
    }

    public OneOf<Success, ValidationError> Cancel()
    {
        if (Status != ReplicationStatus.Pending)
            return new ValidationError(Errors.StatusField, Errors.StatusNotPending);

        Status = ReplicationStatus.Canceled;
        UpdatedAt = DateTime.UtcNow;
        return new Success();
    }

    public OneOf<Success, ValidationError> MarkProcessed()
    {
        if (Status != ReplicationStatus.Pending)
            return new ValidationError(Errors.StatusField, Errors.StatusNotPending);

        Status = ReplicationStatus.Processed;
        UpdatedAt = DateTime.UtcNow;
        return new Success();
    }

    public static class Errors
    {
        public const string UserIdField = "userId";
        public const string UserIdRequired = "User ID is required.";

        public const string DeviceIdField = "deviceId";
        public const string DeviceIdRequired = "Device ID is required.";

        public const string TypeField = "type";
        public const string TypeRequired = "Type is required.";

        public const string StatusField = "status";
        public const string StatusNotPending = "Only pending replications can be updated.";
    }
}
