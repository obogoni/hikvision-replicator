namespace HikvisionReplicator.Api.Domain;

public enum ReplicationStatus
{
    Pending,
    InProgress,
    Succeeded,
    Failed,
    Superseded,
}
