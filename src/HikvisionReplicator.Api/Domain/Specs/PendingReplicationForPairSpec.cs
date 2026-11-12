using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// Newer intent already queued for one (spectator, reader) pair — what an attempt that did not
/// land must look for before going back to the queue (WRK-20). The pending index allows one
/// such row per pair, and the attempt being recorded is in progress rather than pending, so
/// any row this finds is the one that overtook it.
/// </summary>
public sealed class PendingReplicationForPairSpec : Specification<Replication>
{
    public PendingReplicationForPairSpec(int userId, int deviceId)
    {
        Query.Where(replication =>
            replication.UserId == userId
            && replication.DeviceId == deviceId
            && replication.Status == ReplicationStatus.Pending
        );
    }
}
