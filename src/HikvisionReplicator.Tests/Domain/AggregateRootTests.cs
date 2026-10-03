using System.Reflection;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Tests.Domain;

public class AggregateRootTests
{
    private sealed record Something : IDomainEvent;

    private sealed record SomethingElse : IDomainEvent;

    private sealed class Spectator : AggregateRoot
    {
        public void Decide(IDomainEvent what) => Raise(what);
    }

    // ─── AD-042: the aggregate carries what it decided, in the order it decided it ───

    [Fact]
    public void An_aggregate_that_decided_nothing_carries_no_events()
    {
        var aggregate = new Spectator();

        Assert.Empty(aggregate.DomainEvents);
    }

    [Fact]
    public void Raised_events_are_carried_on_the_aggregate_in_order()
    {
        var aggregate = new Spectator();
        var first = new Something();
        var second = new SomethingElse();

        aggregate.Decide(first);
        aggregate.Decide(second);

        Assert.Equal<IDomainEvent>([first, second], aggregate.DomainEvents);
    }

    // ─── AD-042: events are added only through the protected helper ───

    [Fact]
    public void Domain_events_cannot_be_added_through_the_exposed_collection()
    {
        var aggregate = new Spectator();
        aggregate.Decide(new Something());

        var exposed = (ICollection<IDomainEvent>)aggregate.DomainEvents;

        Assert.Throws<NotSupportedException>(() => exposed.Add(new SomethingElse()));
        Assert.Single(aggregate.DomainEvents);
    }

    [Fact]
    public void Raising_an_event_is_not_reachable_from_outside_the_aggregate()
    {
        var raise = typeof(AggregateRoot).GetMethod(
            "Raise",
            BindingFlags.Instance | BindingFlags.NonPublic
        );

        Assert.NotNull(raise);
        Assert.True(raise.IsFamily);
    }

    // ─── AD-042: clearing is what a successful save does ───

    [Fact]
    public void Clearing_domain_events_empties_the_collection()
    {
        var aggregate = new Spectator();
        aggregate.Decide(new Something());
        aggregate.Decide(new SomethingElse());

        aggregate.ClearDomainEvents();

        Assert.Empty(aggregate.DomainEvents);
    }
}
