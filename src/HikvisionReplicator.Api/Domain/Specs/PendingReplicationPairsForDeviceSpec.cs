using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The spectators who already have outstanding work queued for one reader — the pairs a
/// backfill expansion must leave alone, because a live intent outranks a backfill one
/// (REP-19, REP-46).
/// <para>
/// <b>Pending only</b>, and ids only. The expansion reads this once for a whole roster, so
/// returning aggregates would materialise the queue to compute a set difference.
/// </para>
/// </summary>
public sealed class PendingReplicationPairsForDeviceSpec : Specification<Replication, int>
{
    public PendingReplicationPairsForDeviceSpec(int deviceId)
    {
        Query
            .Where(replication =>
                replication.DeviceId == deviceId
                && replication.Status == ReplicationStatus.Pending
            )
            .Select(replication => replication.UserId);
    }
}
