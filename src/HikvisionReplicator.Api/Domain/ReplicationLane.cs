namespace HikvisionReplicator.Api.Domain;

/// <summary>
/// Exactly two lanes, so there is exactly one preemption relationship to reason about
/// (AD-038): a spectator at the turnstile never queues behind a fleet-wide backfill.
/// </summary>
public enum ReplicationLane
{
    Live,
    Bulk,
}
