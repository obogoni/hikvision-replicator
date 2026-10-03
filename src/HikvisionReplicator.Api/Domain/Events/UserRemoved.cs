using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A spectator was tombstoned. Unreachable for one already tombstoned, which is what keeps
/// a repeated removal from fanning out twice (REP-42).
/// </summary>
public sealed record UserRemoved(int UserId, DateTime OccurredAt) : IDomainEvent;
