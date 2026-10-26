using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// One reader's serial claim → call → record cycle (design § DeviceLoop). Serial is the point:
/// one loop per reader means one operation in flight per reader inside an instance by
/// construction, and the in-progress index makes it hold across instances (WRK-06).
/// <para>
/// Every cycle runs in a fresh DI scope, so each claim reads the queue as it is now and no
/// change tracker outlives the work it tracked. Every write goes through the aggregate's own
/// transitions and the repository's save — no raw SQL moves a row (AD-005, AD-006).
/// </para>
/// </summary>
public sealed partial class DeviceLoop(
    IServiceScopeFactory scopeFactory,
    IDeviceClient deviceClient,
    IEncryptionService encryption,
    ReplicationOutcomeRecorder recorder,
    IOptions<ReplicationWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<DeviceLoop> logger
)
{
    /// <summary>
    /// Drains one reader until <paramref name="stoppingToken"/> is cancelled. Waits one idle
    /// poll interval whenever it finds nothing to take (WRK-31). A cycle that throws is logged
    /// and waited out the same way, so one bad row never ends a reader's loop (WRK-07).
    /// </summary>
    public async Task RunAsync(int deviceId, CancellationToken stoppingToken)
    {
        var settings = options.Value;

        while (!stoppingToken.IsCancellationRequested)
        {
            bool tookWork;
            try
            {
                tookWork = await TakeNextAsync(deviceId, settings, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                LogCycleFailed(logger, deviceId, exception);
                tookWork = false;
            }

            if (tookWork)
                continue;

            try
            {
                await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Claims, delivers and records one piece of work. <c>false</c> when there was none.</summary>
    private async Task<bool> TakeNextAsync(
        int deviceId,
        ReplicationWorkerOptions settings,
        CancellationToken stoppingToken
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var replications = services.GetRequiredService<IReplicationRepository>();

        // Live, and only then Bulk: two queries rather than one ordered by lane, because the
        // stored words sort Bulk first (WRK-05).
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var work =
            await replications.FirstOrDefaultAsync(
                new NextEligibleReplicationSpec(deviceId, ReplicationLane.Live, now),
                stoppingToken
            )
            ?? await replications.FirstOrDefaultAsync(
                new NextEligibleReplicationSpec(deviceId, ReplicationLane.Bulk, now),
                stoppingToken
            );

        if (work is null)
            return false;

        // In hand before the reader hears of it (WRK-03).
        work.Begin(now);
        await replications.SaveChangesAsync(stoppingToken);

        var device = await services
            .GetRequiredService<IDeviceRepository>()
            .GetByIdAsync(deviceId, stoppingToken);
        if (device is null)
            return false;

        var users = services.GetRequiredService<IUserRepository>();

        // Never null: a spectator is tombstoned, never deleted, and the foreign key restricts.
        var user = (await users.GetByIdAsync(work.UserId, stoppingToken))!;

        if (work.Operation is not ReplicationOperation.Remove)
            await users.LoadPictureAsync(user, stoppingToken);

        var endpoint = new DeviceEndpoint(
            device.Id,
            device.IpAddress.Value,
            device.HttpPort.Value,
            device.Username,
            encryption.Decrypt(device.EncryptedPassword)
        );

        var outcome = await CallAsync(work, user, endpoint, settings, stoppingToken);

        await recorder.RecordAsync(replications, work, outcome, stoppingToken);
        return true;
    }

    /// <summary>
    /// One call to the reader, bounded by <see cref="ReplicationWorkerOptions.DeviceCallTimeout"/>.
    /// A call still unanswered at the deadline is cancelled and counts as unreachable (WRK-11);
    /// a cancellation that came from the host stopping is the host's, and propagates.
    /// </summary>
    private async Task<AttemptOutcome> CallAsync(
        Replication work,
        User user,
        DeviceEndpoint endpoint,
        ReplicationWorkerOptions settings,
        CancellationToken stoppingToken
    )
    {
        using var deadline = new CancellationTokenSource(settings.DeviceCallTimeout, timeProvider);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(
            stoppingToken,
            deadline.Token
        );

        try
        {
            var answer = work.Operation switch
            {
                ReplicationOperation.Add => await deviceClient.AddAsync(
                    endpoint,
                    EnrolmentOf(user),
                    call.Token
                ),
                ReplicationOperation.Update => await deviceClient.UpdateAsync(
                    endpoint,
                    EnrolmentOf(user),
                    call.Token
                ),
                _ => await deviceClient.RemoveAsync(endpoint, user.ExternalRef.Value, call.Token),
            };

            return answer.Match(
                delivered => AttemptOutcome.Delivered,
                rejected => AttemptOutcome.Rejected,
                unreachable => AttemptOutcome.Unreachable
            );
        }
        catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
        {
            return AttemptOutcome.Unreachable;
        }
    }

    /// <summary>What an <c>Add</c> or <c>Update</c> carries: the reference, the PIN and the face (WRK-02).</summary>
    private static DeviceEnrolment EnrolmentOf(User user) =>
        new(
            user.ExternalRef.Value,
            user.AccessCode.Value,
            user.Picture?.Content
                ?? throw new InvalidOperationException(
                    $"User {user.Id} has no face picture to enrol."
                )
        );

    [LoggerMessage(
        EventId = 10,
        Level = LogLevel.Error,
        Message = "A delivery cycle for device {DeviceId} failed; the loop continues."
    )]
    private static partial void LogCycleFailed(ILogger logger, int deviceId, Exception exception);
}
