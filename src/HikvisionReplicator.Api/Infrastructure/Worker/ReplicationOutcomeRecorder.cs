using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// Turns how a call ended into exactly one state for its replication, saved once (design §
/// ReplicationOutcomeRecorder). What the state is, is <see cref="OutcomeDecision"/>'s to decide;
/// this applies it to the aggregate through its own transitions and commits it.
/// </summary>
public sealed partial class ReplicationOutcomeRecorder(
    IServiceScopeFactory scopeFactory,
    IOptions<ReplicationWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ReplicationOutcomeRecorder> logger
)
{
    private readonly RetrySchedule _schedule = new(
        options.Value.RetryBaseDelay,
        options.Value.RetryMaxDelay,
        options.Value.MaxAttempts
    );

    /// <summary>
    /// Records the outcome of the attempt on <paramref name="replication"/>, which is the row as
    /// the loop claimed it, tracked by <paramref name="replications"/>' unit of work.
    /// <paramref name="reason"/> is what the reader said when it did not accept, or why it said
    /// nothing; it is kept as the row's last error (REP-33).
    /// </summary>
    public Task RecordAsync(
        IReplicationRepository replications,
        Replication replication,
        AttemptOutcome outcome,
        string? reason,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(replications);
        ArgumentNullException.ThrowIfNull(replication);

        return SettleAsync(
            replications,
            replication,
            outcome,
            reason ?? outcome.ToString(),
            cancellationToken
        );
    }

    /// <summary>
    /// Puts back work whose call was cut off before the reader answered — the worker stopping
    /// (WRK-23) or the reader's loop being stopped — exactly as an unanswered call is put back
    /// (WRK-11). Read afresh in a unit of work of its own, because the one that claimed it may
    /// have been mid-query when it was cancelled.
    /// </summary>
    public Task ReleaseInterruptedAsync(
        int replicationId,
        string reason,
        CancellationToken cancellationToken
    ) =>
        SettleInHandAsync(
            replicationId,
            reason,
            replication => replication.Status == ReplicationStatus.InProgress,
            cancellationToken
        );

    /// <summary>
    /// Puts back work a crashed worker left in hand past its lease (WRK-22), with nothing
    /// counted. Re-checked on the fresh read: a row released and claimed again since the sweep
    /// listed it carries a new lease, and is left alone.
    /// </summary>
    public Task ReleaseExpiredAsync(
        int replicationId,
        DateTime cutoff,
        CancellationToken cancellationToken
    ) =>
        SettleInHandAsync(
            replicationId,
            "The worker holding this work stopped answering for it; its lease expired.",
            replication =>
                replication is { Status: ReplicationStatus.InProgress } && replication.UpdatedAt < cutoff,
            cancellationToken
        );

    private async Task SettleInHandAsync(
        int replicationId,
        string reason,
        Func<Replication, bool> stillOwed,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var replications = scope.ServiceProvider.GetRequiredService<IReplicationRepository>();

        var replication = await replications.GetByIdAsync(replicationId, cancellationToken);
        if (replication is null || !stillOwed(replication))
        {
            LogOutcomeDiscarded(logger, replicationId, AttemptOutcome.Unreachable);
            return;
        }

        await SettleAsync(
            replications,
            replication,
            AttemptOutcome.Unreachable,
            reason,
            cancellationToken
        );
    }

    /// <summary>
    /// An attempt that did not land first looks for newer intent for the pair, and ends there
    /// rather than going back to the queue beside it (WRK-20). Newer intent that lands after the
    /// look and before the save is caught by the pending index, and the attempt is superseded
    /// all the same.
    /// </summary>
    private async Task SettleAsync(
        IReplicationRepository replications,
        Replication replication,
        AttemptOutcome outcome,
        string said,
        CancellationToken cancellationToken
    )
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var newerPendingExists =
            outcome != AttemptOutcome.Delivered
            && await replications.AnyAsync(
                new PendingReplicationForPairSpec(replication.UserId, replication.DeviceId),
                cancellationToken
            );

        Apply(replication, outcome, said, newerPendingExists, now);

        try
        {
            await replications.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (replications.TranslateIfDuplicatePending(exception).IsT1)
        {
            // The newer intent committed between the look and this save, and this save tried to
            // put the attempt back beside it. Nothing of it was written.
            await SupersedeOvertakenAsync(replication.Id, outcome, said, cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The save affected no row: the reader was deleted mid-call and its work went with
            // it (REP-16), or another instance settled the row first. Either way there is no
            // outcome left to record, and nothing about it is a fault (WRK-24).
            LogOutcomeDiscarded(logger, replication.Id, outcome);
        }
    }

    /// <summary>
    /// Ends work in hand without a call (WRK-21): an <c>Add</c> or <c>Update</c> for a spectator
    /// tombstoned since it was queued. Their face is gone (USR-32) and their <c>Remove</c> is
    /// queued behind it (REP-03), so nothing is lost and no attempt is counted.
    /// </summary>
    public async Task SupersedeWithoutCallAsync(
        IReplicationRepository replications,
        Replication replication,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(replications);
        ArgumentNullException.ThrowIfNull(replication);

        replication.SupersedeAfterAttempt(timeProvider.GetUtcNow().UtcDateTime);

        try
        {
            await replications.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The reader was deleted since the claim (WRK-24).
            LogSupersedeDiscarded(logger, replication.Id);
        }
    }

    /// <summary>
    /// The pending-index race, settled: the row is read again as the database holds it — still
    /// in hand, since this worker holds it — and superseded with this attempt's cost on it.
    /// </summary>
    private async Task SupersedeOvertakenAsync(
        int replicationId,
        AttemptOutcome outcome,
        string said,
        CancellationToken cancellationToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var replications = scope.ServiceProvider.GetRequiredService<IReplicationRepository>();

        var replication = await replications.GetByIdAsync(replicationId, cancellationToken);
        if (replication is not { Status: ReplicationStatus.InProgress })
        {
            LogOutcomeDiscarded(logger, replicationId, outcome);
            return;
        }

        Apply(
            replication,
            outcome,
            said,
            newerPendingExists: true,
            timeProvider.GetUtcNow().UtcDateTime
        );

        try
        {
            await replications.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            LogOutcomeDiscarded(logger, replicationId, outcome);
        }
    }

    private void Apply(
        Replication replication,
        AttemptOutcome outcome,
        string said,
        bool newerPendingExists,
        DateTime now
    )
    {
        var action = OutcomeDecision.AfterCall(
            outcome,
            replication.AttemptCount,
            newerPendingExists,
            _schedule,
            now
        );

        switch (action)
        {
            // The reader holds the spectator now (WRK-03).
            case ReplicationAction.Succeed:
                replication.Succeed(now);
                break;

            // The reader never judged it: back to the queue at no cost, in the place it held
            // (WRK-11) — its creation time is untouched, so it is still the reader's head item.
            case ReplicationAction.Release:
                replication.Release(said, now);
                break;

            // The reader refused it: the refusal is counted and kept (REP-30, REP-33), and the
            // record waits out its backoff on the row, where a restart cannot lose it (WRK-16).
            case ReplicationAction.RetryAt retry:
                replication.Fail(said, now);
                replication.Retry(now, retry.NotBefore);
                break;

            // Refused for the last time: Failed, and nothing ever takes it again (WRK-17).
            case ReplicationAction.DeadLetter:
                replication.Fail(said, now);
                break;

            // Overtaken by newer intent: it ends here, keeping what this attempt cost (WRK-20).
            // A refusal counts and is kept, through Failed; an unanswered call counts nothing
            // and keeps its reason, through Release — whose Pending is never saved, because
            // Supersede ends it in the same unit of work.
            // SPEC_DEVIATION: the unanswered case goes Release → Supersede rather than through
            // SupersedeAfterAttempt as the design names.
            // Reason: SupersedeAfterAttempt keeps the row's previous last error, and WRK-20
            // requires the last error of this attempt; the saved transition is the same,
            // InProgress → Superseded.
            case ReplicationAction.SupersedeAfterAttempt when outcome == AttemptOutcome.Rejected:
                replication.Fail(said, now);
                replication.SupersedeAfterAttempt(now);
                break;

            case ReplicationAction.SupersedeAfterAttempt:
                replication.Release(said, now);
                replication.Supersede(now);
                break;

            default:
                throw new InvalidOperationException($"No call ends in {action}.");
        }
    }

    [LoggerMessage(
        EventId = 21,
        Level = LogLevel.Information,
        Message = "Replication {ReplicationId} ended {Outcome}, but is no longer in hand; the outcome is discarded."
    )]
    private static partial void LogOutcomeDiscarded(
        ILogger logger,
        int replicationId,
        AttemptOutcome outcome
    );

    [LoggerMessage(
        EventId = 22,
        Level = LogLevel.Information,
        Message = "Replication {ReplicationId} was to be set aside without a call, but is no longer in hand; nothing is recorded."
    )]
    private static partial void LogSupersedeDiscarded(ILogger logger, int replicationId);
}
