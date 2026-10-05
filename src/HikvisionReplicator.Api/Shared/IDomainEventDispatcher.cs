namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Runs every handler registered for every event an aggregate raised, once each.
/// <para>
/// It is called from inside <c>AppDbContext.SaveChangesAsync</c>, <b>before</b> <c>base</c>,
/// so whatever a handler stages joins the save that was already happening (AD-042, REP-07).
/// Handlers stage and never save (AD-041), so this returns having written nothing.
/// </para>
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> events, CancellationToken cancellationToken);
}
