using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Domain.Events;

/// <summary>
/// A spectator entered the registry. Every reader is owed their face.
/// <para>
/// Carries the aggregate, not its key: this is raised from inside <see cref="User.Create"/>,
/// where <see cref="User.Id"/> is still <c>0</c> because the database generates it. A handler
/// staging a row from that id would write a foreign key to a row that does not exist.
/// </para>
/// </summary>
public sealed record UserRegistered(User User, DateTime OccurredAt) : IDomainEvent;
