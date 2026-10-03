using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Events;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

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
public partial class ReplicationFanOut(
    AppDbContext context,
    IDeviceRepository devices,
    IUserRepository users,
    IReplicationRepository replications,
    ReplicationMetrics metrics,
    ILogger<ReplicationFanOut> logger
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
    /// Turns a reader's recorded debt into the work it stands for: one bulk-lane enrolment
    /// per active spectator, and the debt settled. Returns how many rows were staged.
    /// <para>
    /// <b>Nothing in this feature calls this.</b> It is a directly-invocable operation
    /// because AD-039 ships the queue runner-agnostic: whatever drains the queue decides
    /// when a backfill is expanded, and that decision is feature 4's. Like everything else
    /// here it stages and never saves (AD-041), so the caller's own save is what commits
    /// the roster and the settled debt together.
    /// </para>
    /// <para>
    /// Finding no outstanding debt is the answer to two different questions at once: the
    /// reader was deleted and the cascade took its debt with it (REP-44), or the debt has
    /// already been expanded (REP-20). Both mean nothing is owed, so both stage nothing and
    /// neither is an error.
    /// </para>
    /// </summary>
    public async Task<int> ExpandBackfillAsync(
        int deviceId,
        DateTime now,
        CancellationToken cancellationToken
    )
    {
        // Queried through the specification rather than an inline predicate (AD-006). There
        // is no repository for intents because nothing else reads them: this operation is
        // their only consumer.
        var debt = await context
            .BackfillIntents.WithSpecification(new PendingBackfillIntentForDeviceSpec(deviceId))
            .FirstOrDefaultAsync(cancellationToken);

        if (debt is null)
            return 0;

        var roster = await users.ListAsync(new ActiveUserIdsSpec(), cancellationToken);

        // A live intent outranks a backfill one (REP-19). The spectator whose ticket was
        // bought while the reader was being registered is already owed an Add on the live
        // lane, and a Bulk duplicate would both lose to the pending index and demote the
        // work AD-038 wants to preempt with.
        var alreadyOwed = await replications.ListAsync(
            new PendingReplicationPairsForDeviceSpec(deviceId),
            cancellationToken
        );

        var owed = roster.Except(alreadyOwed).ToList();

        foreach (var userId in owed)
        {
            context.Replications.Add(
                Replication.Create(
                    userId,
                    deviceId,
                    ReplicationOperation.Add,
                    ReplicationLane.Bulk,
                    now
                )
            );
            metrics.Enqueued(ReplicationOperation.Add, ReplicationLane.Bulk);
        }

        // Terminal, so a second invocation finds no outstanding debt and stages nothing.
        debt.MarkExpanded(now);

        return owed.Count;
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
        {
            superseded.Supersede(now);
            metrics.Superseded();
        }

        foreach (var deviceId in readers)
        {
            context.Replications.Add(
                Replication.Create(user, deviceId, operation, ReplicationLane.Live, now)
            );
            metrics.Enqueued(operation, ReplicationLane.Live);
        }

        // One record per write, not per row: the question REP-38 answers is what one
        // spectator costs the catalogue.
        metrics.FanOut(readers.Count);

        // Only an arrival raises the roster: an amendment leaves it where it was and a
        // removal lowers it, so neither can push a reader over its ceiling.
        if (operation == ReplicationOperation.Add)
            await SignalReadersTheRosterOutgrewAsync(cancellationToken);
    }

    /// <summary>
    /// REP-24. The spectator is <b>accepted regardless</b> — refusing a ticket-holder at the
    /// turnstile because a reader somewhere else is full is the failure this service exists to
    /// prevent, and AD-021 puts the capacity guard at fleet admission for exactly that reason.
    /// What crossing a ceiling produces is a signal to an operator, not an error to an
    /// integrator.
    /// <para>
    /// Every reader the roster has outgrown is named, not just the one that tipped over
    /// first: each is a separate piece of hardware that has to be swapped, and an operator
    /// told about one of three learns the wrong size of the problem.
    /// </para>
    /// </summary>
    private async Task SignalReadersTheRosterOutgrewAsync(CancellationToken cancellationToken)
    {
        // Dispatch runs before the save (AD-042), so the spectator who provoked this is not
        // in the table yet. The number an operator needs is the one this write is about to
        // make true: the stored count plus the one arriving.
        var roster = await users.CountAsync(new ActiveUserCountSpec(), cancellationToken) + 1;

        var ceilings = await devices.ListAsync(new ReaderCeilingsSpec(), cancellationToken);

        foreach (var reader in ceilings.Where(reader => reader.FaceCapacity.Value < roster))
        {
            LogRosterOutgrewReader(logger, reader.DeviceId, reader.FaceCapacity.Value, roster);
            metrics.CapacityExceeded(reader.DeviceId);
        }
    }

    /// <summary>
    /// All three numbers are in the line because the counter of REP-37 can only carry the
    /// reader: an active-user count is unbounded cardinality, and a metric tag that grows
    /// with the crowd is a bill, not a signal.
    /// </summary>
    [LoggerMessage(
        EventId = 1,
        Level = LogLevel.Warning,
        Message = "Device {DeviceId} holds {FaceCapacity} faces, but {ActiveUserCount} users are now active."
    )]
    private static partial void LogRosterOutgrewReader(
        ILogger logger,
        int deviceId,
        int faceCapacity,
        int activeUserCount
    );
}
