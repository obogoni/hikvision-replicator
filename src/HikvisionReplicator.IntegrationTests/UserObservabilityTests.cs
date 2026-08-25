using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// USR-40 and USR-41: the one CPU-bound step on the latency path AD-014 makes primary is
/// visible — as a child span of the request that provoked it, and as duration and size metrics.
/// <para>
/// One registration is made during setup, with every listener already attached, and all five
/// assertions read what it produced. Doing the work once is what lets the measurement
/// assertions say <em>exactly one</em> rather than <em>at least one</em>.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserObservabilityTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";
    private const string ExternalRef = "observed-spectator";

    /// <summary>The source ASP.NET Core publishes its server spans on.</summary>
    private const string AspNetCoreSource = "Microsoft.AspNetCore";

    private readonly InMemorySpanSink _spanSink = new();

    /// <summary>
    /// The listener a <c>TracerProvider</c> installs is process-wide, so this sink also receives
    /// spans from every other host alive in the process. Correlating on a trace this class alone
    /// provokes is what makes "the normalization span" a well-defined thing to assert on
    /// (<c>docs/test-patterns.md</c>).
    /// </summary>
    private readonly ActivityTraceId _testTraceId = ActivityTraceId.CreateRandom();

    private readonly List<double> _durationMeasurements = [];
    private readonly List<int> _byteSizeMeasurements = [];

    private WebApplicationFactory<Program> _factory = null!;
    private int _storedFaceByteSize;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            // Host settings are the only layer in place early enough for the value Program.cs
            // reads while assembling the builder — without it no tracer is registered at all.
            builder.UseSetting(OtlpEndpointKey, "http://localhost:4317");

            builder.ConfigureServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing =>
                    tracing.AddInMemoryExporter(_spanSink)
                )
            );
        });

        // The application publishes its instruments through the container's own meter factory,
        // which caches by name — so asking for the same name here yields the very meter the
        // normalizer will use, and reference equality filters out every other host's.
        var meter = _factory.Services.GetRequiredService<IMeterFactory>()
            .Create(SkiaFaceImageNormalizer.MeterName);

        using (var listener = new MeterListener())
        {
            listener.InstrumentPublished = (instrument, subscription) =>
            {
                if (ReferenceEquals(instrument.Meter, meter))
                    subscription.EnableMeasurementEvents(instrument);
            };
            listener.SetMeasurementEventCallback<double>(
                (instrument, measurement, _, _) =>
                {
                    if (instrument.Name == SkiaFaceImageNormalizer.DurationMetricName)
                        _durationMeasurements.Add(measurement);
                }
            );
            listener.SetMeasurementEventCallback<int>(
                (instrument, measurement, _, _) =>
                {
                    if (instrument.Name == SkiaFaceImageNormalizer.ByteSizeMetricName)
                        _byteSizeMeasurements.Add(measurement);
                }
            );
            listener.Start();

            using var client = _factory.CreateClient();
            _storedFaceByteSize = await RegisterSpectatorAsync(client, _testTraceId);
        }

        await WaitForRequestSpanAsync();
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<int> RegisterSpectatorAsync(
        HttpClient client,
        ActivityTraceId traceId
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Put,
            $"/api/users/{Uri.EscapeDataString(ExternalRef)}"
        )
        {
            Content = JsonContent.Create(
                new
                {
                    name = "Ada Lovelace",
                    accessCode = "778899",
                    facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
                }
            ),
        };

        // W3C propagation: the server span adopts this trace, which is what lets the assertions
        // below tell this request's spans apart from every other host's.
        request.Headers.Add("traceparent", $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("faceByteSize").GetInt32();
    }

    /// <summary>
    /// A server span is exported only once the request pipeline has fully unwound, which can
    /// happen after the client has already been handed the response.
    /// </summary>
    private async Task WaitForRequestSpanAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline && RequestSpans().Count == 0)
            await Task.Delay(50);
    }

    private List<Activity> RequestSpans() =>
        [
            .. _spanSink.Spans.Where(span =>
                span.Source.Name == AspNetCoreSource && span.TraceId == _testTraceId
            ),
        ];

    private List<Activity> NormalizationSpans() =>
        [
            .. _spanSink.Spans.Where(span =>
                span.Source.Name == SkiaFaceImageNormalizer.ActivitySourceName
                && span.TraceId == _testTraceId
            ),
        ];

    // ─── USR-40: the request is traced ───────────────────────────────────

    [Fact]
    public void Handled_user_request_produces_a_span_naming_the_route_that_served_it()
    {
        var requestSpan = Assert.Single(RequestSpans());

        Assert.Equal(ActivityKind.Server, requestSpan.Kind);
        Assert.Equal("PUT /api/users/{externalRef}", requestSpan.DisplayName);
    }

    // ─── USR-40: normalization is a distinct child of it ─────────────────

    [Fact]
    public void Normalizing_a_face_picture_is_traced_as_a_child_of_the_request_that_caused_it()
    {
        var requestSpan = Assert.Single(RequestSpans());
        var normalizationSpan = Assert.Single(NormalizationSpans());

        Assert.Equal(SkiaFaceImageNormalizer.NormalizationSpanName, normalizationSpan.DisplayName);
        Assert.Equal(requestSpan.SpanId, normalizationSpan.ParentSpanId);
        Assert.NotEqual(requestSpan.SpanId, normalizationSpan.SpanId);
    }

    [Fact]
    public void Normalization_span_records_how_long_the_work_took()
    {
        var normalizationSpan = Assert.Single(NormalizationSpans());

        Assert.True(
            normalizationSpan.Duration > TimeSpan.Zero,
            $"Expected a measured duration, got {normalizationSpan.Duration}."
        );
    }

    // ─── USR-41: the same work is measured as metrics ────────────────────

    [Fact]
    public void Normalizing_a_face_picture_records_how_long_it_took()
    {
        var duration = Assert.Single(_durationMeasurements);

        Assert.True(duration > 0, $"Expected a positive duration, got {duration}.");
    }

    [Fact]
    public void Normalizing_a_face_picture_records_the_size_of_the_stored_derivative()
    {
        var byteSize = Assert.Single(_byteSizeMeasurements);

        Assert.Equal(_storedFaceByteSize, byteSize);
    }
}
