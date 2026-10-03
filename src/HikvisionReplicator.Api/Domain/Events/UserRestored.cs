using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A tombstoned spectator was resurrected. Deliberately not <see cref="UserChanged"/>: the
/// readers no longer hold that face, so the work owed is an add, not an update (REP-06).
/// </summary>
public sealed record UserRestored(int UserId, DateTime OccurredAt) : IDomainEvent;
