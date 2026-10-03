namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Something that has to happen because an aggregate said something happened. One handler
/// may close this interface over several event types — the fan-out does — which is why
/// there is deliberately no non-generic base here: a shared base would give a multi-event
/// handler two inherited implementations of the same method and no way to choose between
/// them.
/// </summary>
/// <typeparam name="TEvent">The event this handler answers to.</typeparam>
public interface IDomainEventHandler<in TEvent>
    where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken);
}
