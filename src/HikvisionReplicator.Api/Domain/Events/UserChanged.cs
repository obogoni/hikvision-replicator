using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A spectator's stored representation actually differs from what it was. Raised only from
/// the branch that already decided that (USR-26), so an upsert that changes nothing raises
/// nothing and queues nothing (REP-04).
/// </summary>
public sealed record UserChanged(int UserId, DateTime OccurredAt) : IDomainEvent;
