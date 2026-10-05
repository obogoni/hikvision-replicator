using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The outstanding work a spectator already has queued, across every reader — the rows a
/// newer intent supersedes (REP-08).
/// <para>
/// <b>Pending only.</b> Terminal rows are the intent log and must stay exactly as they were
/// (REP-10), and an <c>InProgress</c> row is in a device's hands: superseding it would leave
/// the queue claiming a write was replaced while it was still being made (REP-12).
/// </para>
/// <para>
/// This is the one queue read that returns whole aggregates rather than a projection,
/// because the caller transitions them.
/// </para>
/// </summary>
public sealed class PendingReplicationsForUserSpec : Specification<Replication>
{
    public PendingReplicationsForUserSpec(int userId)
    {
        Query.Where(replication =>
            replication.UserId == userId && replication.Status == ReplicationStatus.Pending
        );
    }
}
