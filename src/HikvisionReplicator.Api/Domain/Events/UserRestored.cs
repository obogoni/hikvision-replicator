using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A tombstoned spectator was resurrected. Deliberately not <see cref="UserChanged"/>: the
/// readers no longer hold that face, so the work owed is an add, not an update (REP-06).
/// <para>
/// Carries the aggregate rather than its key, for the same reason every event here does: one
/// payload shape, and the handler never needs a re-read.
/// </para>
/// </summary>
public sealed record UserRestored(User User, DateTime OccurredAt) : IDomainEvent;
