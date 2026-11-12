using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The one piece of work a reader should take next from one lane: pending, past any rejection
/// backoff, oldest first (WRK-05, WRK-16).
/// <para>
/// <b>One lane per query, never ordered by lane.</b> The lane is stored as a word and
/// <c>Bulk</c> sorts before <c>Live</c>, so an <c>ORDER BY</c> on it would invert AD-038's
/// priority. The worker asks for <c>Live</c> and only then for <c>Bulk</c>.
/// </para>
/// <para>
/// The id breaks ties between rows created in the same instant — a fan-out stages every
/// reader's row with one clock reading — so the order is total and a reader never sees two
/// equally old rows in a different order from one look to the next.
/// </para>
/// </summary>
public sealed class NextEligibleReplicationSpec : Specification<Replication>
{
    public NextEligibleReplicationSpec(int deviceId, ReplicationLane lane, DateTime now)
    {
        Query
            .Where(replication =>
                replication.DeviceId == deviceId
                && replication.Lane == lane
                && replication.Status == ReplicationStatus.Pending
                && (replication.NextAttemptAt == null || replication.NextAttemptAt <= now)
            )
            .OrderBy(replication => replication.CreatedAt)
            .ThenBy(replication => replication.Id)
            .Take(1);
    }
}
