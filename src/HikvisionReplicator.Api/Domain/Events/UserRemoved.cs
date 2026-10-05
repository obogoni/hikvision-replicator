using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A spectator was tombstoned. Unreachable for one already tombstoned, which is what keeps
/// a repeated removal from fanning out twice (REP-42).
/// <para>
/// Carries the aggregate rather than its key, for the same reason every event here does: one
/// payload shape, and the handler never needs a re-read.
/// </para>
/// </summary>
public sealed record UserRemoved(User User, DateTime OccurredAt) : IDomainEvent;
