using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// Turns how a call ended into exactly one state for its replication, saved once (design §
/// ReplicationOutcomeRecorder). What the state is, is <see cref="OutcomeDecision"/>'s to decide;
/// this applies it to the aggregate through its own transitions and commits it.
/// </summary>
public sealed partial class ReplicationOutcomeRecorder(
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
    public async Task RecordAsync(
        IReplicationRepository replications,
        Replication replication,
        AttemptOutcome outcome,
        string? reason,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(replications);
        ArgumentNullException.ThrowIfNull(replication);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var action = OutcomeDecision.AfterCall(
            outcome,
            replication.AttemptCount,
            newerPendingExists: false,
            _schedule,
            now
        );

        switch (action)
        {
            // The reader holds the spectator now (WRK-03).
            case ReplicationAction.Succeed:
                replication.Succeed(now);
                await replications.SaveChangesAsync(cancellationToken);
                break;

            // The reader never judged it: back to the queue at no cost, in the place it held
            // (WRK-11) — its creation time is untouched, so it is still the reader's head item.
            case ReplicationAction.Release:
                replication.Release(reason ?? outcome.ToString(), now);
                await replications.SaveChangesAsync(cancellationToken);
                break;

            // Retry and dead letter are recorded by the failure-handling tasks that follow. Until then the row stays InProgress, which is what a crashed
            // worker leaves behind too, and nothing is lost.
            default:
                LogOutcomeNotRecorded(logger, replication.Id, outcome);
                break;
        }
    }

    [LoggerMessage(
        EventId = 20,
        Level = LogLevel.Warning,
        Message = "Replication {ReplicationId} ended {Outcome}, which is not recorded yet."
    )]
    private static partial void LogOutcomeNotRecorded(
        ILogger logger,
        int replicationId,
        AttemptOutcome outcome
    );
}
