using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Events;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The one place every fan-out rule lives: what a change to the registry or the catalogue
/// owes the queue.
/// <para>
/// <b>It stages and never saves</b> (AD-041). Every row it adds goes into the change tracker
/// the calling slice already owns, and the slice's own single <c>SaveChangesAsync</c> commits
/// the spectator and the work owed for them together — which is the whole of REP-07. A save
/// here would make that two transactions and leave a crash between them undetectable until a
/// turnstile.
/// </para>
/// <para>
/// Nothing in it decides <em>whether</em> anything happened: the aggregate already did, by
/// raising the event or not. That is why an upsert changing nothing queues nothing (REP-04)
/// and a second removal queues nothing (REP-42) without a guard anywhere in this file.
/// </para>
/// </summary>
public class ReplicationFanOut(
    AppDbContext context,
    IDeviceRepository devices,
    IReplicationRepository replications
)
    : IDomainEventHandler<UserRegistered>,
        IDomainEventHandler<UserRestored>,
        IDomainEventHandler<UserChanged>,
        IDomainEventHandler<UserRemoved>,
        IDomainEventHandler<DeviceRegistered>
{
    public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return StageForEveryReaderAsync(
            domainEvent.User,
            ReplicationOperation.Add,
            domainEvent.OccurredAt,
            cancellationToken
        );
    }

    /// <summary>
    /// A resurrection owes an <c>Add</c>, not an <c>Update</c>: the removal destroyed the
    /// face and no reader still holds it, so there is nothing there to correct (REP-06).
    /// </summary>
    public Task HandleAsync(UserRestored domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return StageForEveryReaderAsync(
            domainEvent.User,
            ReplicationOperation.Add,
            domainEvent.OccurredAt,
            cancellationToken
        );
    }

    public Task HandleAsync(UserChanged domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return StageForEveryReaderAsync(
            domainEvent.User,
            ReplicationOperation.Update,
            domainEvent.OccurredAt,
            cancellationToken
        );
    }

    public Task HandleAsync(UserRemoved domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        return StageForEveryReaderAsync(
            domainEvent.User,
            ReplicationOperation.Remove,
            domainEvent.OccurredAt,
            cancellationToken
        );
    }

    /// <summary>
    /// A new reader is owed the whole active roster, and that debt is <b>one row</b> — not
    /// one per spectator (REP-14). At 50,000 spectators the naive form puts a 50,000-row
    /// insert inside the HTTP request an operator is waiting on at the turnstile.
    /// <para>
    /// The expansion that turns the debt into work is deliberately not called from here:
    /// it is invoked directly, by whatever ends up draining the queue (AD-039).
    /// </para>
    /// <para>
    /// The aggregate is carried into the row rather than its key, for the same reason the
    /// user path does it — the event is raised inside the factory, before the database has
    /// issued one.
    /// </para>
    /// </summary>
    public Task HandleAsync(DeviceRegistered domainEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);

        context.BackfillIntents.Add(
            BackfillIntent.Create(domainEvent.Device, domainEvent.OccurredAt)
        );

        return Task.CompletedTask;
    }

    /// <summary>
    /// One piece of outstanding work per reader in the catalogue, in the live lane — AD-038's
    /// preemption relationship is "Live before Bulk", and a spectator at a turnstile is the
    /// reason the lane exists.
    /// <para>
    /// The aggregate is carried into the row rather than its key: on the registration path
    /// the event is raised inside the factory, where the database has not issued a key yet
    /// and the id is still <c>0</c>. The mapped navigation is what makes EF write the key it
    /// did issue.
    /// </para>
    /// </summary>
    private async Task StageForEveryReaderAsync(
        User user,
        ReplicationOperation operation,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        // Ids only. This read happens on every user write, the path AD-038 measures, so
        // materialising the catalogue to use its keys would put twenty aggregates through
        // the change tracker for nothing.
        var readers = await devices.ListAsync(new RegisteredDeviceIdsSpec(), cancellationToken);

        // Room is made before the new intent is staged. The queue is an intent log, not a
        // history of keystrokes: a spectator edited five times before kickoff owes each
        // reader one piece of work, not five (REP-08). The pending index is what enforces
        // that — this is not a read-then-write check standing in for it, it is the
        // supersession the index leaves room for (AD-022).
        //
        // The specification returns Pending rows only, which is where REP-12 comes from:
        // work already in a reader's hands is left to reach its own terminal status, and
        // the new intent is inserted beside it. The partial index permits exactly that.
        var outstanding = await replications.ListAsync(
            new PendingReplicationsForUserSpec(user.Id),
            cancellationToken
        );

        foreach (var superseded in outstanding)
            superseded.Supersede(now);

        foreach (var deviceId in readers)
        {
            context.Replications.Add(
                Replication.Create(user, deviceId, operation, ReplicationLane.Live, now)
            );
        }
    }
}
