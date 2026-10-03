using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A spectator's stored representation actually differs from what it was. Raised only from
/// the branch that already decided that (USR-26), so an upsert that changes nothing raises
/// nothing and queues nothing (REP-04).
/// <para>
/// Carries the aggregate rather than its key, for the same reason every event here does: one
/// payload shape, and the handler never needs a re-read.
/// </para>
/// </summary>
public sealed record UserChanged(User User, DateTime OccurredAt) : IDomainEvent;
