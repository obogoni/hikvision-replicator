namespace HikvisionReplicator.Api.Shared;

public interface IAggregateRoot
{
    int Id { get; }
    DateTime CreatedAt { get; }
    DateTime UpdatedAt { get; }
}
