using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A reader entered the catalogue and is owed the whole active roster. There is no
/// counterpart for an update: a backfill is owed on registration only (REP-14).
/// <para>
/// Carries the aggregate, not its key: this is raised from inside <see cref="Device.Create"/>,
/// where <see cref="Device.Id"/> is still <c>0</c> because the database generates it. An intent
/// staged from that id would reference a reader that does not exist.
/// </para>
/// </summary>
public sealed record DeviceRegistered(Device Device, DateTime OccurredAt) : IDomainEvent;
