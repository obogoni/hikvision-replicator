using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>A spectator entered the registry. Every reader is owed their face.</summary>
public sealed record UserRegistered(int UserId, DateTime OccurredAt) : IDomainEvent;
