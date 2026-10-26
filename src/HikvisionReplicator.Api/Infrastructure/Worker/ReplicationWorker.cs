using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// The supervisor (design § ReplicationWorker): expands every outstanding backfill and keeps one
/// <see cref="DeviceLoop"/> running per reader in the catalogue. A purpose-built hosted worker inside the API rather than a job
/// runner — the queue already is a table with its own states, lane and supersession (AD-043).
/// <para>
/// Each reader gets its own loop so a slow or offline reader never holds up another (WRK-07),
/// and the loop set is re-read every <see cref="ReplicationWorkerOptions.PollInterval"/>.
/// A failed tick is logged and the next one runs; nothing the supervisor does can stop a loop
/// that is already draining.
/// </para>
/// </summary>
public sealed partial class ReplicationWorker(
    IServiceScopeFactory scopeFactory,
    DeviceLoop deviceLoop,
    IOptions<ReplicationWorkerOptions> options,
    TimeProvider timeProvider,
    ILogger<ReplicationWorker> logger
) : BackgroundService
{
    private readonly Dictionary<int, (CancellationTokenSource Stop, Task Running)> _loops = [];

    /// <summary>Loops told to stop and not yet seen to finish, awaited at shutdown.</summary>
    private readonly List<(CancellationTokenSource Stop, Task Running)> _stopping = [];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;

        // SPEC_DEVIATION: the worker is always registered and stands down here when disabled,
        // rather than being registered only when ReplicationWorker:Enabled is true.
        // Reason: Program.cs reads builder configuration before WebApplicationFactory and other
        // late configuration sources are applied, so a registration-time check would silently
        // ignore an override layered later. Bound options see every source.
        if (!settings.Enabled)
            return;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var owed = await ExpandBackfillsAsync(stoppingToken);
                    await SyncLoopsAsync(owed, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    LogTickFailed(logger, exception);
                }

                try
                {
                    await Task.Delay(settings.PollInterval, timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        finally
        {
            // Every loop's token is linked to the stopping token, so this waits for each to
            // finish the cycle it is in.
            var every = _loops.Values.Concat(_stopping).ToList();
            await Task.WhenAll(every.Select(loop => loop.Running));
            foreach (var loop in every)
                loop.Stop.Dispose();
        }
    }

    /// <summary>
    /// Turns every outstanding backfill debt into the reader's Bulk work, one reader and one
    /// save at a time (WRK-04, AD-041). Returns the readers whose debt is still outstanding
    /// afterwards.
    /// <para>
    /// A save refused because the queue moved under it is not a fault: another instance settled
    /// the same debt first, or a live upsert queued the same spectator in between (WRK-34).
    /// Nothing of the refused save is committed, and the next tick reads the queue afresh.
    /// </para>
    /// </summary>
    private async Task<HashSet<int>> ExpandBackfillsAsync(CancellationToken stoppingToken)
    {
        HashSet<int> owed;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            owed = (
                await scope
                    .ServiceProvider.GetRequiredService<AppDbContext>()
                    .BackfillIntents.WithSpecification(new PendingBackfillDeviceIdsSpec())
                    .ToListAsync(stoppingToken)
            ).ToHashSet();
        }

        foreach (var deviceId in owed.ToList())
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var services = scope.ServiceProvider;

            try
            {
                await services
                    .GetRequiredService<ReplicationFanOut>()
                    .ExpandBackfillAsync(
                        deviceId,
                        timeProvider.GetUtcNow().UtcDateTime,
                        stoppingToken
                    );
                await services.GetRequiredService<AppDbContext>().SaveChangesAsync(stoppingToken);
                owed.Remove(deviceId);
            }
            catch (DbUpdateException exception) when (QueueMovedUnder(services, exception))
            {
                // Settled elsewhere, or raced by a live write; read again next tick.
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // One reader's failing debt must not hold up every other reader's (WRK-07).
                LogBackfillFailed(logger, deviceId, exception);
            }
        }

        return owed;
    }

    private static bool QueueMovedUnder(IServiceProvider services, DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException
        || services
            .GetRequiredService<IReplicationRepository>()
            .TranslateIfDuplicatePending(exception)
            .IsT1;

    /// <summary>
    /// Starts a loop for every reader that has none and stops the loop of every reader that has
    /// left the catalogue, so a change takes effect within one poll interval without a restart
    /// (WRK-10). A stopped loop starts no further call (WRK-25).
    /// <para>
    /// <b>A reader still owed its backfill gets no loop yet.</b> The expansion skips every
    /// spectator with live work already pending for the reader (REP-19); a loop that delivered
    /// that live work first would take it out of Pending, and the expansion would queue the
    /// same spectator a second time on the bulk lane (WRK-34).
    /// </para>
    /// </summary>
    private async Task SyncLoopsAsync(HashSet<int> owed, CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var readers = (
            await scope
                .ServiceProvider.GetRequiredService<IDeviceRepository>()
                .ListAsync(new RegisteredDeviceIdsSpec(), stoppingToken)
        ).ToHashSet();

        // Told to stop, not waited for: a removed reader's loop may be inside a call, and
        // the tick must not hold every other reader's catalogue change behind it.
        // SPEC_DEVIATION: a call in flight to a removed reader is cancelled with its loop,
        // where the design lets it complete and discards the outcome (WRK-24).
        // Reason: the reader is decommissioned and its rows went with it (REP-16), so there is
        // no outcome left to record — finishing the call would only hold the loop open.
        foreach (var (deviceId, loop) in _loops.Where(loop => !readers.Contains(loop.Key)).ToList())
        {
            await loop.Stop.CancelAsync();
            _stopping.Add(loop);
            _loops.Remove(deviceId);
        }

        foreach (var finished in _stopping.Where(loop => loop.Running.IsCompleted).ToList())
        {
            finished.Stop.Dispose();
            _stopping.Remove(finished);
        }

        foreach (
            var deviceId in readers.Where(deviceId =>
                !_loops.ContainsKey(deviceId) && !owed.Contains(deviceId)
            )
        )
        {
            var stop = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            _loops[deviceId] = (
                stop,
                Task.Run(() => deviceLoop.RunAsync(deviceId, stop.Token), CancellationToken.None)
            );
        }
    }

    [LoggerMessage(
        EventId = 31,
        Level = LogLevel.Error,
        Message = "Expanding the backfill owed to device {DeviceId} failed; the next tick will retry."
    )]
    private static partial void LogBackfillFailed(ILogger logger, int deviceId, Exception exception);

    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Error,
        Message = "A replication worker tick failed; the next tick will retry."
    )]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
