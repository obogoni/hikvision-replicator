using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// Work taken in hand before <c>cutoff</c> and never finished — what a crashed worker leaves
/// behind (WRK-22). The claim stamps <see cref="Replication.UpdatedAt"/>, and nothing touches an
/// in-progress row until its outcome is recorded, so the stamp is when the lease began. Ids
/// only: each row is re-read and settled in a unit of work of its own.
/// </summary>
public sealed class ExpiredLeasesSpec : Specification<Replication, int>
{
    public ExpiredLeasesSpec(DateTime cutoff)
    {
        Query
            .Where(replication =>
                replication.Status == ReplicationStatus.InProgress
                && replication.UpdatedAt < cutoff
            )
            .Select(replication => replication.Id);
    }
}
