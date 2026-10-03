using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// The plumbing that carries what an aggregate decided to whatever has to act on it
/// (AD-042). Pure logic: the container is real, the handlers are fakes, and nothing here
/// touches a database.
/// </summary>
public class DomainEventDispatcherTests
{
    private sealed record Ticked(int Sequence) : IDomainEvent;

    private sealed record Tocked : IDomainEvent;

    private sealed class RecordingHandler<TEvent> : IDomainEventHandler<TEvent>
        where TEvent : IDomainEvent
    {
        public List<TEvent> Received { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
        {
            Received.Add(domainEvent);
            Tokens.Add(cancellationToken);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Records every service type asked for, which is the only way to assert that a
    /// dispatcher resolved <em>nothing</em> rather than resolving an empty handler list.
    /// </summary>
    private sealed class RecordingServiceProvider(IServiceProvider inner) : IServiceProvider
    {
        public List<Type> Requested { get; } = [];

        public object? GetService(Type serviceType)
        {
            Requested.Add(serviceType);
            return inner.GetService(serviceType);
        }
    }

    private static RecordingServiceProvider ProviderWith(Action<IServiceCollection> register)
    {
        var services = new ServiceCollection();
        register(services);
        return new RecordingServiceProvider(services.BuildServiceProvider());
    }

    [Fact]
    public async Task Every_handler_registered_for_an_event_receives_it_exactly_once()
    {
        var first = new RecordingHandler<Ticked>();
        var second = new RecordingHandler<Ticked>();
        var provider = ProviderWith(services =>
        {
            services.AddSingleton<IDomainEventHandler<Ticked>>(first);
            services.AddSingleton<IDomainEventHandler<Ticked>>(second);
        });
        var raised = new Ticked(1);

        await new DomainEventDispatcher(provider).DispatchAsync([raised], CancellationToken.None);

        Assert.Same(raised, Assert.Single(first.Received));
        Assert.Same(raised, Assert.Single(second.Received));
    }

    [Fact]
    public async Task Every_event_in_a_batch_reaches_its_handler()
    {
        var handler = new RecordingHandler<Ticked>();
        var provider = ProviderWith(services =>
            services.AddSingleton<IDomainEventHandler<Ticked>>(handler)
        );

        await new DomainEventDispatcher(provider).DispatchAsync(
            [new Ticked(1), new Ticked(2), new Ticked(3)],
            CancellationToken.None
        );

        Assert.Equal([1, 2, 3], handler.Received.Select(ticked => ticked.Sequence));
    }

    [Fact]
    public async Task A_handler_for_a_different_event_is_left_alone()
    {
        var ticks = new RecordingHandler<Ticked>();
        var tocks = new RecordingHandler<Tocked>();
        var provider = ProviderWith(services =>
        {
            services.AddSingleton<IDomainEventHandler<Ticked>>(ticks);
            services.AddSingleton<IDomainEventHandler<Tocked>>(tocks);
        });

        await new DomainEventDispatcher(provider).DispatchAsync(
            [new Ticked(1)],
            CancellationToken.None
        );

        Assert.Single(ticks.Received);
        Assert.Empty(tocks.Received);
    }

    [Fact]
    public async Task An_event_with_no_handler_registered_is_a_no_op()
    {
        var provider = ProviderWith(_ => { });

        await new DomainEventDispatcher(provider).DispatchAsync(
            [new Ticked(1)],
            CancellationToken.None
        );

        Assert.Contains(typeof(IEnumerable<IDomainEventHandler<Ticked>>), provider.Requested);
    }

    [Fact]
    public async Task A_batch_with_no_events_resolves_no_handler()
    {
        var handler = new RecordingHandler<Ticked>();
        var provider = ProviderWith(services =>
            services.AddSingleton<IDomainEventHandler<Ticked>>(handler)
        );

        await new DomainEventDispatcher(provider).DispatchAsync([], CancellationToken.None);

        Assert.Empty(provider.Requested);
        Assert.Empty(handler.Received);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_every_handler()
    {
        var first = new RecordingHandler<Ticked>();
        var second = new RecordingHandler<Tocked>();
        var provider = ProviderWith(services =>
        {
            services.AddSingleton<IDomainEventHandler<Ticked>>(first);
            services.AddSingleton<IDomainEventHandler<Tocked>>(second);
        });
        using var source = new CancellationTokenSource();

        await new DomainEventDispatcher(provider).DispatchAsync(
            [new Ticked(1), new Tocked()],
            source.Token
        );

        Assert.Equal(source.Token, Assert.Single(first.Tokens));
        Assert.Equal(source.Token, Assert.Single(second.Tokens));
    }
}
