namespace HikvisionReplicator.Api.Shared;

public interface IAggregateRoot
{
    int Id { get; }
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }

    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

/// <summary>
/// Carries the domain events an aggregate raised (AD-042, extending AD-005). The collection
/// is read-only to callers and <see cref="Raise"/> is protected, so only the aggregate that
/// decided something happened can say so.
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _domainEvents = [];

    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();

    public void ClearDomainEvents() => _domainEvents.Clear();

    protected void Raise(IDomainEvent domainEvent) => _domainEvents.Add(domainEvent);
}
