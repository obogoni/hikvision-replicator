using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// The supervisor (design § ReplicationWorker): keeps one <see cref="DeviceLoop"/> running per
/// reader in the catalogue. A purpose-built hosted worker inside the API rather than a job
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
    private readonly Dictionary<int, Task> _loops = [];

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
                    await SyncLoopsAsync(stoppingToken);
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
            // Every loop observes the same stopping token, so this waits for each to finish
            // the cycle it is in.
            await Task.WhenAll(_loops.Values);
        }
    }

    /// <summary>Starts a loop for every reader that has none (WRK-10).</summary>
    private async Task SyncLoopsAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var readers = await scope
            .ServiceProvider.GetRequiredService<IDeviceRepository>()
            .ListAsync(new RegisteredDeviceIdsSpec(), stoppingToken);

        foreach (var deviceId in readers)
        {
            if (_loops.ContainsKey(deviceId))
                continue;

            _loops[deviceId] = Task.Run(
                () => deviceLoop.RunAsync(deviceId, stoppingToken),
                CancellationToken.None
            );
        }
    }

    [LoggerMessage(
        EventId = 30,
        Level = LogLevel.Error,
        Message = "A replication worker tick failed; the next tick will retry."
    )]
    private static partial void LogTickFailed(ILogger logger, Exception exception);
}
