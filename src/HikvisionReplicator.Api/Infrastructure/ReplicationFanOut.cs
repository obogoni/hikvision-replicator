using HikvisionReplicator.Api.Domain.Events;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The one place every fan-out rule lives: what a change to the catalogue or the registry
/// owes the queue.
/// <para>
/// <b>It stages and never saves</b> (AD-041). Every row it adds goes into the change tracker
/// the calling slice already owns, and the slice's own single <c>SaveChangesAsync</c> commits
/// the spectator and the work owed for them together — which is the whole of REP-07.
/// </para>
/// <para>
/// It handles every event the two aggregates raise, so that a write never passes through
/// here silently: a type with no handler is refused at startup, not discovered at a
/// turnstile.
/// </para>
/// </summary>
public class ReplicationFanOut
    : IDomainEventHandler<UserRegistered>,
        IDomainEventHandler<UserRestored>,
        IDomainEventHandler<UserChanged>,
        IDomainEventHandler<UserRemoved>,
        IDomainEventHandler<DeviceRegistered>
{
    public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task HandleAsync(UserRestored domainEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task HandleAsync(UserChanged domainEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task HandleAsync(UserRemoved domainEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task HandleAsync(DeviceRegistered domainEvent, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
