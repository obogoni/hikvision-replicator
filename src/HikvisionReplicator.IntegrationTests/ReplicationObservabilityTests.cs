using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What the queue tells an operator while it fills. REP-24 and REP-25: a spectator whose
/// arrival takes the roster past a reader's ceiling is <b>accepted anyway</b>, and the reader
/// is named in a warning instead. REP-35…REP-39: the same writes move the queue's counters,
/// and a configured deployment is actually subscribed to them.
/// <para>
/// A cross-cutting concern, so the class is named for the concern rather than for a use case
/// (<c>docs/test-patterns.md</c>). Each test arranges its own catalogue because the ceiling
/// under test is the arrangement.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ReplicationObservabilityTests(PostgresFixture fixture)
    : IAsyncLifetime,
        IDisposable
{
    /// <summary>
    /// The sink receives every line the host emits, so the fan-out's own warnings are picked
    /// out by category — the alternative, filtering on the sentence, would be an assertion
    /// that selects the text it then claims to verify.
    /// </summary>
    private const string FanOutWarning =
        "HikvisionReplicator.Api.Infrastructure.ReplicationFanOut [Warning]";

    private const string Password = "s3cr3t-Passw0rd";

    private readonly InMemoryLogSink _logSink = new();
    private readonly List<Measured> _measurements = [];

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private MeterListener _listener = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        // A sink per test class, on a host of its own: the lines another class provokes never
        // reach it, so "exactly one warning" is a claim about this test's traffic.
        _factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(_logSink))
        );
        _client = _factory.CreateClient();
        _listener = ListenToReplicationMeterOf(_factory);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _listener?.Dispose();
        _client?.Dispose();
        _factory?.Dispose();
        _logSink.Dispose();
    }

    /// <summary>
    /// The host publishes through its container's own meter factory, which caches by name —
    /// so asking it for the same name yields the very meter the application records on, and
    /// reference equality keeps every other host's measurements out.
    /// </summary>
    private MeterListener ListenToReplicationMeterOf(WebApplicationFactory<Program> factory)
    {
        var meter = factory
            .Services.GetRequiredService<IMeterFactory>()
            .Create(ReplicationMetrics.MeterName);

        var listener = new MeterListener
        {
            InstrumentPublished = (instrument, subscription) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                    subscription.EnableMeasurementEvents(instrument);
            },
        };

        listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags)
        );
        listener.SetMeasurementEventCallback<int>(
            (instrument, measurement, tags, _) => Capture(instrument.Name, measurement, tags)
        );
        listener.Start();

        return listener;
    }

    private void Capture(
        string instrument,
        long value,
        ReadOnlySpan<KeyValuePair<string, object?>> tags
    )
    {
        var captured = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var tag in tags)
            captured[tag.Key] = tag.Value;

        lock (_measurements)
            _measurements.Add(new Measured(instrument, value, captured));
    }

    private List<Measured> MeasurementsOf(string instrument)
    {
        lock (_measurements)
            return [.. _measurements.Where(measured => measured.Instrument == instrument)];
    }

    private sealed record Measured(
        string Instrument,
        long Value,
        Dictionary<string, object?> Tags
    );

    // ─── REP-24: the spectator is accepted, the reader is named ──────────

    [Fact]
    public async Task Spectator_who_takes_the_roster_past_a_reader_is_still_accepted()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 2);
        await GivenSpectatorsHaveArrivedAsync(2);

        var response = await ArriveAsync(3);

        // The whole point of AD-021's fleet-admission guard: a ticket-holder at a turnstile
        // is never the one refused for hardware somebody else has to replace.
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// All three numbers are in the line, because the counter of REP-37 can carry only the
    /// reader: an active-user count is unbounded cardinality and cannot be a metric tag.
    /// The sentence is pinned literally — an operator reads it to decide which reader to swap.
    /// </summary>
    [Fact]
    public async Task Reader_the_roster_outgrew_is_named_with_its_ceiling_and_the_new_count()
    {
        var reader = await GivenRegisteredReaderAsync(faceCapacity: 2);
        await GivenSpectatorsHaveArrivedAsync(2);

        await ArriveAsync(3);

        var warning = Assert.Single(CapacityWarnings());
        Assert.Contains(
            $"Device {reader} holds 2 faces, but 3 users are now active.",
            warning,
            StringComparison.Ordinal
        );
    }

    /// <summary>
    /// The count is the one the write is <em>about to</em> make true. Dispatch runs before the
    /// save, so a signal reading the table alone would report the crowd one spectator short —
    /// and would stay silent altogether on the arrival that crosses the ceiling.
    /// </summary>
    [Fact]
    public async Task Arrival_that_exactly_crosses_a_ceiling_is_the_one_that_signals()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 2);

        await GivenSpectatorsHaveArrivedAsync(2);
        Assert.Empty(CapacityWarnings());

        await ArriveAsync(3);

        Assert.Single(CapacityWarnings());
    }

    [Fact]
    public async Task Only_the_readers_the_roster_outgrew_are_named()
    {
        var outgrown = await GivenRegisteredReaderAsync(faceCapacity: 2, ipAddress: "10.0.0.1");
        var roomy = await GivenRegisteredReaderAsync(faceCapacity: 10, ipAddress: "10.0.0.2");

        await GivenSpectatorsHaveArrivedAsync(2);
        await ArriveAsync(3);

        var warning = Assert.Single(CapacityWarnings());
        Assert.Contains($"Device {outgrown} holds 2 faces", warning, StringComparison.Ordinal);
        Assert.DoesNotContain($"Device {roomy} ", warning, StringComparison.Ordinal);
    }

    // ─── REP-25: silence while the fleet can hold the crowd ──────────────

    [Fact]
    public async Task No_reader_is_named_while_every_one_of_them_can_hold_the_roster()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 5);

        await GivenSpectatorsHaveArrivedAsync(5);

        Assert.Empty(CapacityWarnings());
    }

    private List<string> CapacityWarnings() =>
        [
            .. _logSink.Lines.Where(line =>
                line.StartsWith(FanOutWarning, StringComparison.Ordinal)
            ),
        ];

    private async Task<int> GivenRegisteredReaderAsync(
        int faceCapacity,
        string ipAddress = "192.168.1.10"
    )
    {
        var response = await _client.PostAsJsonAsync(
            "/api/devices",
            new
            {
                name = "Front Gate Reader",
                ipAddress,
                httpPort = 80,
                username = "admin",
                password = Password,
                faceCapacity,
            }
        );
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("id").GetInt32();
    }

    private async Task GivenSpectatorsHaveArrivedAsync(int count)
    {
        for (var ticket = 1; ticket <= count; ticket++)
            Assert.Equal(HttpStatusCode.Created, (await ArriveAsync(ticket)).StatusCode);
    }

    /// <summary>
    /// Through the route, not seeded into the table: the signal hangs off the fan-out the
    /// write provokes, and a spectator who never went through it provokes nothing.
    /// </summary>
    private Task<HttpResponseMessage> ArriveAsync(int ticket, string name = "Ada Lovelace") =>
        _client.PutAsJsonAsync(
            $"/api/users/TICKET-{ticket}",
            new
            {
                name,
                accessCode = $"77880{ticket}",
                facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
            }
        );

    // ─── REP-35…REP-38: the queue moves its own counters ─────────────────

    [Fact]
    public async Task Work_put_on_the_queue_is_counted_by_operation_and_lane()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 10);

        await ArriveAsync(1);

        var enqueued = Assert.Single(MeasurementsOf(ReplicationMetrics.EnqueuedMetricName));
        Assert.Equal(1, enqueued.Value);
        Assert.Equal("Add", enqueued.Tags[ReplicationMetrics.OperationTag]);
        Assert.Equal("Live", enqueued.Tags[ReplicationMetrics.LaneTag]);
    }

    /// <summary>
    /// The operation tag is what the runner reads to know whether a row enrols a face or
    /// destroys one, so a counter that tagged every write the same way would be worse than
    /// none — it would read as a fleet enrolling spectators it is in fact deleting.
    /// </summary>
    [Fact]
    public async Task Amending_a_spectator_is_counted_as_update_work()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 10);
        await ArriveAsync(1);

        await ArriveAsync(1, name: "Grace Hopper");

        Assert.Equal(
            ["Add", "Update"],
            MeasurementsOf(ReplicationMetrics.EnqueuedMetricName)
                .Select(enqueued => enqueued.Tags[ReplicationMetrics.OperationTag])
        );
    }

    [Fact]
    public async Task Work_replaced_by_a_later_intent_is_counted_as_superseded()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 10);
        await ArriveAsync(1);

        await ArriveAsync(1, name: "Grace Hopper");

        var superseded = Assert.Single(MeasurementsOf(ReplicationMetrics.SupersededMetricName));
        Assert.Equal(1, superseded.Value);
    }

    [Fact]
    public async Task Nothing_is_counted_as_superseded_when_there_was_no_outstanding_work()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 10);

        await ArriveAsync(1);

        Assert.Empty(MeasurementsOf(ReplicationMetrics.SupersededMetricName));
    }

    /// <summary>
    /// REP-37's counter carries the reader and nothing else — the count that would make it
    /// actionable is in REP-24's log line precisely because it cannot be a tag.
    /// </summary>
    [Fact]
    public async Task Reader_the_roster_outgrew_is_counted_against_that_reader()
    {
        var reader = await GivenRegisteredReaderAsync(faceCapacity: 2);
        await GivenSpectatorsHaveArrivedAsync(2);

        await ArriveAsync(3);

        var exceeded = Assert.Single(
            MeasurementsOf(ReplicationMetrics.CapacityExceededMetricName)
        );
        Assert.Equal(1, exceeded.Value);
        Assert.Equal(
            reader,
            Assert.IsType<int>(exceeded.Tags[ReplicationMetrics.DeviceTag])
        );
        Assert.DoesNotContain("count", exceeded.Tags.Keys, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// REP-38 records the per-write cost of AD-038's twenty-reader envelope: one measurement
    /// per spectator write, valued at the number of readers it owed work to.
    /// </summary>
    [Fact]
    public async Task A_spectator_write_records_how_many_readers_it_owed_work_to()
    {
        await GivenRegisteredReaderAsync(faceCapacity: 10, ipAddress: "10.0.0.1");
        await GivenRegisteredReaderAsync(faceCapacity: 10, ipAddress: "10.0.0.2");

        await ArriveAsync(1);

        var fanOut = Assert.Single(MeasurementsOf(ReplicationMetrics.FanOutSizeMetricName));
        Assert.Equal(2, fanOut.Value);
    }

    // ─── REP-39: the deployment actually collects them ───────────────────

    /// <summary>
    /// L-037, and the reason this criterion exists at all: every assertion above observes the
    /// instruments through a listener <em>the test installs</em>, which passes just as happily
    /// when production has no reader for them. This one asserts the other half — that a
    /// configured deployment's own metrics provider is subscribed to the meter — by exporting
    /// through the provider rather than listening beside it. Drop
    /// <c>AddMeter(ReplicationMetrics.MeterName)</c> from <c>Program.cs</c> and only this test
    /// fails.
    /// </summary>
    [Fact]
    public async Task Configured_deployment_collects_the_queue_metrics()
    {
        var exported = new List<Metric>();

        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            // The value Program.cs reads while assembling the builder — without it no
            // metrics provider is registered at all.
            builder.UseSetting("OpenTelemetry:OtlpEndpoint", "http://localhost:4317");

            builder.ConfigureServices(services =>
                services.ConfigureOpenTelemetryMeterProvider(metrics =>
                    metrics.AddInMemoryExporter(exported)
                )
            );
        });

        using (var client = factory.CreateClient())
        {
            var reader = await client.PostAsJsonAsync(
                "/api/devices",
                new
                {
                    name = "Front Gate Reader",
                    ipAddress = "192.168.1.10",
                    httpPort = 80,
                    username = "admin",
                    password = Password,
                    faceCapacity = 10,
                }
            );
            Assert.Equal(HttpStatusCode.Created, reader.StatusCode);

            var arrival = await client.PutAsJsonAsync(
                "/api/users/TICKET-1",
                new
                {
                    name = "Ada Lovelace",
                    accessCode = "778801",
                    facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
                }
            );
            Assert.Equal(HttpStatusCode.Created, arrival.StatusCode);
        }

        factory.Services.GetRequiredService<MeterProvider>().ForceFlush();

        Assert.Contains(
            exported,
            metric => metric.Name == ReplicationMetrics.EnqueuedMetricName
        );
    }
}
