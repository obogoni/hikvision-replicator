using System.Collections.ObjectModel;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What the worker tells an operator while it drains (WRK-26…WRK-29): how long a spectator took
/// to reach a reader — the live-lane p95 is AD-038's reading — how each attempt ended, which
/// records were set aside, and when a reader was paused and when it came back.
/// <para>
/// A cross-cutting concern, so the class is named for it. Every instrument is read through the
/// deployment's own metrics provider, exporting to memory, never through a listener the test
/// installs beside it: a listener passes just as happily when production collects nothing
/// (L-037, REP-39).
/// </para>
/// <para>
/// <b>Whose traffic is measured.</b> Each test runs a host of its own, on a database and a fleet
/// of its own, so every delivery the provider sees comes from that test. The shared worker host
/// is reset before each test and has nothing to deliver, and no other collection runs a worker
/// at all. Where a value can be pinned exactly — the delivery duration, on the injected clock —
/// it is.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class WorkerObservabilityTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 10, 26, 18, 45, 0, TimeSpan.Zero);

    private const string WorkerCategory = "HikvisionReplicator.Api.Infrastructure.Worker.";

    private readonly ExportedMetrics _exported = new();

    /// <summary>
    /// What the in-memory reader has exported. It exports on its own short period, on a thread
    /// of its own, so the test polls rather than forcing a flush: a forced flush flushes every
    /// reader the deployment has, including its OTLP exporter, which waits out its own timeout
    /// against an endpoint nobody listens on.
    /// </summary>
    private sealed class ExportedMetrics : Collection<MetricSnapshot>
    {
        private readonly Lock _gate = new();

        public MetricSnapshot? Latest(string instrument)
        {
            lock (_gate)
                return this.LastOrDefault(snapshot => snapshot.Name == instrument);
        }

        protected override void InsertItem(int index, MetricSnapshot item)
        {
            lock (_gate)
                base.InsertItem(index, item);
        }
    }

    /// <summary>A worker host whose deployment collects metrics, into <see cref="_exported"/>.</summary>
    private Task<WorkerWebApplicationFactory> StartCollectingHostAsync() =>
        Fixture.StartIsolatedHostAsync(
            new Dictionary<string, string?>(),
            builder =>
            {
                // The value Program.cs reads while assembling the builder — without it no
                // metrics provider is registered at all.
                builder.UseSetting("OpenTelemetry:OtlpEndpoint", "http://localhost:4317");
                builder.ConfigureServices(services =>
                    services.ConfigureOpenTelemetryMeterProvider(metrics =>
                        metrics.AddInMemoryExporter(
                            _exported,
                            reader =>
                                reader.PeriodicExportingMetricReaderOptions.ExportIntervalMilliseconds =
                                    50
                        )
                    )
                );
            }
        );

    /// <summary>
    /// The latest export of each point of <paramref name="instrument"/>. The provider exports
    /// cumulative totals, so the last snapshot holds everything recorded so far.
    /// </summary>
    private List<MetricPoint> Exported(string instrument) =>
        _exported.Latest(instrument) is { } latest ? [.. latest.MetricPoints] : [];

    private static object? Tag(MetricPoint point, string key)
    {
        foreach (var tag in point.Tags)
        {
            if (tag.Key == key)
                return tag.Value;
        }

        return null;
    }

    private Task<List<MetricPoint>> ExportedUntilAsync(
        string instrument,
        Func<List<MetricPoint>, bool> done,
        string because
    ) => Eventually.UntilAsync(() => Task.FromResult(Exported(instrument)), done, because);

    private static long CountOf(List<MetricPoint> points, string key, string value) =>
        points.Where(point => Equals(Tag(point, key), value)).Sum(point => point.GetSumLong());

    // ─── WRK-26: creation to success, by lane and operation ───

    /// <summary>
    /// Two spectators queued at kickoff on one reader, which takes them one at a time. The first
    /// is answered three seconds later, which is also when the second is taken in hand; the
    /// second is answered seven seconds after kickoff. Measured from queueing, that is 3 s and
    /// 7 s — ten in all. Measured from the claim it would be 3 s and 4 s, so the second row's
    /// wait in the queue is what pins the interval's start to its creation.
    /// </summary>
    [Fact]
    public async Task A_delivery_records_the_seconds_from_queueing_to_success_by_lane_and_operation()
    {
        await using var host = await StartCollectingHostAsync();
        using var client = host.CreateClient();
        var reader = await GivenRegisteredDeviceAsync("10.0.8.1", via: client);
        host.Readers.SetLatency(reader, TimeSpan.FromMilliseconds(500));

        host.Clock.Freeze(Kickoff);
        await UpsertSpectatorAsync("TICKET-1", "100001", via: client);
        await UpsertSpectatorAsync("TICKET-2", "100002", via: client);

        await Eventually.UntilAsync(
            () => Task.FromResult(host.Readers.Calls),
            calls => calls.Count == 1,
            "the first call is with the reader"
        );
        host.Clock.Freeze(Kickoff.AddSeconds(3));

        // The reader is held by the first call, so the second is claimed only once it is
        // answered — on the instant three seconds after kickoff.
        await Eventually.UntilAsync(
            () => Task.FromResult(host.Readers.Calls),
            calls => calls.Count == 2,
            "the second call is with the reader"
        );
        host.Clock.Freeze(Kickoff.AddSeconds(7));

        var durations = await ExportedUntilAsync(
            ReplicationMetrics.DeliveryDurationMetricName,
            points => points.Sum(point => point.GetHistogramCount()) == 2,
            "both deliveries have been measured"
        );

        var duration = Assert.Single(durations);
        Assert.Equal("Live", Tag(duration, ReplicationMetrics.LaneTag));
        Assert.Equal("Add", Tag(duration, ReplicationMetrics.OperationTag));
        Assert.Equal(2, duration.GetHistogramCount());
        Assert.Equal(3.0 + 7.0, duration.GetHistogramSum());

        var attempts = Exported(ReplicationMetrics.AttemptsMetricName);
        Assert.Equal(2, CountOf(attempts, ReplicationMetrics.OutcomeTag, "succeeded"));
    }

    // ─── WRK-27 / WRK-28: refused, then set aside ───

    [Fact]
    public async Task A_record_refused_until_it_is_set_aside_is_counted_as_rejected_attempts_and_one_dead_letter()
    {
        await using var host = await StartCollectingHostAsync();
        using var client = host.CreateClient();
        var reader = await GivenRegisteredDeviceAsync("10.0.8.1", via: client);
        host.Readers.RejectWhen((_, externalRef) => externalRef == "TICKET-1");

        await UpsertSpectatorAsync("TICKET-1", via: client);

        var deadLetter = (
            await Eventually.UntilAsync(
                () => QueuedWorkAsync(host),
                queue => queue.Single().Status == ReplicationStatus.Failed,
                "the record has been set aside"
            )
        ).Single();

        var deadLettered = await ExportedUntilAsync(
            ReplicationMetrics.DeadLetteredMetricName,
            points => points.Sum(point => point.GetSumLong()) > 0,
            "the dead letter has been counted"
        );
        Assert.Equal(1, deadLettered.Sum(point => point.GetSumLong()));

        var attempts = Exported(ReplicationMetrics.AttemptsMetricName);
        Assert.Equal(5, CountOf(attempts, ReplicationMetrics.OutcomeTag, "rejected"));
        Assert.Equal(0, CountOf(attempts, ReplicationMetrics.OutcomeTag, "succeeded"));

        // WRK-28: everything an operator needs to find the record and see why it was refused.
        static bool IsDeadLetterLine(string line) =>
            line.StartsWith(WorkerCategory, StringComparison.Ordinal)
            && line.Contains("dead-letter", StringComparison.OrdinalIgnoreCase);
        var line = Assert.Single(
            await Eventually.UntilAsync(
                () => Task.FromResult(host.Logs.Lines),
                lines => lines.Any(IsDeadLetterLine),
                "the dead letter has been logged"
            ),
            IsDeadLetterLine
        );
        Assert.Contains($"Replication {deadLetter.Id} ", line, StringComparison.Ordinal);
        Assert.Contains($"user {deadLetter.UserId} ", line, StringComparison.Ordinal);
        Assert.Contains($"device {reader} ", line, StringComparison.Ordinal);
        Assert.Contains("after 5 attempts", line, StringComparison.Ordinal);
        Assert.Contains($"Device {reader} refused TICKET-1.", line, StringComparison.Ordinal);
    }

    // ─── WRK-27 / WRK-29: unreachable, paused, resumed ───

    [Fact]
    public async Task A_reader_that_drops_off_and_returns_is_counted_paused_then_resumed()
    {
        await using var host = await StartCollectingHostAsync();
        using var client = host.CreateClient();
        var reader = await GivenRegisteredDeviceAsync("10.0.8.1", via: client);
        host.Readers.SetUnreachable(reader, true);

        await UpsertSpectatorAsync("TICKET-1", via: client);

        var paused = await ExportedUntilAsync(
            ReplicationMetrics.DevicePauseMetricName,
            points => CountOf(points, ReplicationMetrics.TransitionTag, "paused") > 0,
            "the pause has been counted"
        );
        Assert.Equal(1, CountOf(paused, ReplicationMetrics.TransitionTag, "paused"));
        Assert.Equal(0, CountOf(paused, ReplicationMetrics.TransitionTag, "resumed"));
        Assert.True(
            CountOf(
                Exported(ReplicationMetrics.AttemptsMetricName),
                ReplicationMetrics.OutcomeTag,
                "unreachable"
            ) >= 2
        );

        host.Readers.SetUnreachable(reader, false);

        var resumed = await ExportedUntilAsync(
            ReplicationMetrics.DevicePauseMetricName,
            points => CountOf(points, ReplicationMetrics.TransitionTag, "resumed") > 0,
            "the resume has been counted"
        );
        Assert.Equal(1, CountOf(resumed, ReplicationMetrics.TransitionTag, "paused"));
        Assert.Equal(1, CountOf(resumed, ReplicationMetrics.TransitionTag, "resumed"));

        bool IsTransitionLine(string line) =>
            line.StartsWith(WorkerCategory, StringComparison.Ordinal)
            && line.Contains($"Device {reader} ", StringComparison.Ordinal)
            && (
                line.Contains("paused", StringComparison.Ordinal)
                || line.Contains("resumed", StringComparison.Ordinal)
            );
        var transitions = (
            await Eventually.UntilAsync(
                () => Task.FromResult(host.Logs.Lines),
                lines => lines.Count(IsTransitionLine) >= 2,
                "both transitions have been logged"
            )
        )
            .Where(IsTransitionLine)
            .ToList();
        Assert.Equal(2, transitions.Count);
        Assert.Contains("paused", transitions[0], StringComparison.Ordinal);
        Assert.Contains("resumed", transitions[1], StringComparison.Ordinal);
    }
}
