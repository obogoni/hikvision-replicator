namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Something an aggregate decided happened. The aggregate carries its events until a
/// successful save clears them (AD-042), so nothing outside the aggregate has to remember
/// what a write means.
/// </summary>
public interface IDomainEvent
{ }
