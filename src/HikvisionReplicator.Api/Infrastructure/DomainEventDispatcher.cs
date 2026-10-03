using System.Reflection;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// Hand-rolled dispatch: resolve <see cref="IDomainEventHandler{TEvent}"/> from the container
/// and await each one. No MediatR — five events and one handler do not need a pipeline.
/// <para>
/// The one reflection hop exists because the event's type is only known at run time while the
/// handler interface is generic. It resolves nothing at all when there is nothing to dispatch,
/// which is every device read, every list and every get — the overwhelming majority of saves.
/// </para>
/// <para>
/// <b>An event with no registered handler is a silent no-op here, on purpose.</b> Refusing it
/// would make the dispatcher the authority on what must be handled, which is a startup
/// question, not a per-save one — <c>Program.cs</c> answers it before the first request.
/// </para>
/// </summary>
public class DomainEventDispatcher(IServiceProvider serviceProvider) : IDomainEventDispatcher
{
    private static readonly MethodInfo HandleOne = typeof(DomainEventDispatcher).GetMethod(
        nameof(DispatchToHandlersAsync),
        BindingFlags.Instance | BindingFlags.NonPublic
    )!;

    public async Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> events,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(events);

        foreach (var domainEvent in events)
        {
            var dispatch = HandleOne.MakeGenericMethod(domainEvent.GetType());
            await (Task)dispatch.Invoke(this, [domainEvent, cancellationToken])!;
        }
    }

    private async Task DispatchToHandlersAsync<TEvent>(
        TEvent domainEvent,
        CancellationToken cancellationToken
    )
        where TEvent : IDomainEvent
    {
        foreach (var handler in serviceProvider.GetServices<IDomainEventHandler<TEvent>>())
            await handler.HandleAsync(domainEvent, cancellationToken);
    }
}
