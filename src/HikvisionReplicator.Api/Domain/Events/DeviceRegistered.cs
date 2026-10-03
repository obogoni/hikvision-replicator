using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A reader entered the catalogue and is owed the whole active roster. There is no
/// counterpart for an update: a backfill is owed on registration only (REP-14).
/// </summary>
public sealed record DeviceRegistered(int DeviceId, DateTime OccurredAt) : IDomainEvent;
