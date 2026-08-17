using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace HikvisionReplicator.Tests;

/// <summary>Keeps every span the application exports, for inspection.</summary>
public sealed class InMemorySpanSink : ICollection<Activity>
{
    private readonly List<Activity> _spans = [];

    /// <summary>A copy taken while the threads that end spans are held off.</summary>
    public IReadOnlyList<Activity> Spans
    {
        get
        {
            lock (_spans)
                return [.. _spans];
        }
    }

    public int Count
    {
        get
        {
            lock (_spans)
                return _spans.Count;
        }
    }

    public bool IsReadOnly => false;

    public void Add(Activity span)
    {
        lock (_spans)
            _spans.Add(span);
    }

    public void Clear()
    {
        lock (_spans)
            _spans.Clear();
    }

    public bool Contains(Activity span)
    {
        lock (_spans)
            return _spans.Contains(span);
    }

    public void CopyTo(Activity[] array, int arrayIndex)
    {
        lock (_spans)
            _spans.CopyTo(array, arrayIndex);
    }

    public bool Remove(Activity span)
    {
        lock (_spans)
            return _spans.Remove(span);
    }

    public IEnumerator<Activity> GetEnumerator() => Spans.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// DEV-16 clause (a): a handled request really does produce a trace — an HTTP server span with
/// the database work hanging off it — rather than merely a <c>TracerProvider</c> sitting in the
/// container. Spans are collected by attaching an in-memory exporter to the application's own
/// tracer from the test host, so the production composition root is exercised exactly as it
/// ships. Those same spans are then swept for credentials, which is the third leak channel
/// DEV-07 names alongside response bodies and logs.
/// </summary>
[Collection(PostgresCollection.Name)]
public class TracingTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";
    private const string SentinelPassword = "Sentinel-Traced-P@ssw0rd-6c1e2a74";

    /// <summary>The source ASP.NET Core publishes its server spans on.</summary>
    private const string AspNetCoreSource = "Microsoft.AspNetCore";

    /// <summary>The source the EF Core instrumentation publishes its command spans on.</summary>
    private const string EntityFrameworkCoreSource =
        "OpenTelemetry.Instrumentation.EntityFrameworkCore";

    private readonly InMemorySpanSink _spanSink = new();

    /// <summary>
    /// The exporter is attached to a <c>TracerProvider</c>, but the listener a provider installs
    /// on <c>Microsoft.AspNetCore</c> is process-wide — so this sink also receives spans from
    /// every other test host alive in the process, including classes running in a parallel
    /// collection. Correlating on a trace this class alone provokes is what makes "the span" a
    /// well-defined thing to assert on.
    /// </summary>
    private readonly ActivityTraceId _testTraceId = ActivityTraceId.CreateRandom();

    private WebApplicationFactory<Program> _factory = null!;
    private string _storedCiphertext = string.Empty;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            // Host settings are the only layer in place early enough for the value Program.cs
            // reads while assembling the builder — without it no tracer is registered at all.
            builder.UseSetting(OtlpEndpointKey, "http://localhost:4317");

            // Adds a sink to the application's own tracer; production code is untouched.
            builder.ConfigureServices(services =>
                services.ConfigureOpenTelemetryTracerProvider(tracing =>
                    tracing.AddInMemoryExporter(_spanSink)
                )
            );
        });

        using var client = _factory.CreateClient();
        var id = await RegisterDeviceAsync(client, _testTraceId);

        await using var db = fixture.CreateDbContext();
        _storedCiphertext = await db
            .Devices.Where(device => device.Id == id)
            .Select(device => device.EncryptedPassword)
            .SingleAsync();

        await WaitForRequestSpanAsync();
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private static async Task<int> RegisterDeviceAsync(HttpClient client, ActivityTraceId traceId)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices")
        {
            Content = JsonContent.Create(
                new
                {
                    name = "Traced Reader",
                    ipAddress = "192.168.9.11",
                    httpPort = 80,
                    username = "admin",
                    password = SentinelPassword,
                    faceCapacity = 10_000,
                }
            ),
        };

        // W3C propagation: the server span adopts this trace, which is what lets the assertions
        // below tell this request's spans apart from every other host's.
        request.Headers.Add(
            "traceparent",
            $"00-{traceId}-{ActivitySpanId.CreateRandom()}-01"
        );

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("id").GetInt32();
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

    private List<Activity> DatabaseSpans() =>
        [
            .. _spanSink.Spans.Where(span =>
                span.Source.Name == EntityFrameworkCoreSource && span.TraceId == _testTraceId
            ),
        ];

    private List<string> SpanAttributes() =>
        [
            .. _spanSink
                .Spans.SelectMany(span => span.TagObjects)
                .Select(tag => $"{tag.Key}={tag.Value}"),
        ];

    // ─── DEV-16 (a): the request itself is traced ────────────────────────

    [Fact]
    public void Handled_request_produces_a_span_naming_the_route_that_served_it()
    {
        var requestSpan = Assert.Single(RequestSpans());

        Assert.Equal(ActivityKind.Server, requestSpan.Kind);
        Assert.Equal("POST /api/devices", requestSpan.DisplayName);
    }

    // ─── DEV-16 (a): the database work hangs off that span ───────────────

    [Fact]
    public void Database_work_is_traced_as_a_child_of_the_request_that_caused_it()
    {
        var requestSpan = Assert.Single(RequestSpans());

        var databaseSpans = DatabaseSpans()
            .Where(span => span.TraceId == requestSpan.TraceId)
            .ToList();

        Assert.NotEmpty(databaseSpans);
        Assert.All(databaseSpans, span => Assert.Equal(requestSpan.SpanId, span.ParentSpanId));
        Assert.All(databaseSpans, span => Assert.Equal(ActivityKind.Client, span.Kind));
    }

    // ─── DEV-07: the trace is the third place a credential could leak ────

    [Fact]
    public void No_span_attribute_ever_carries_the_password()
    {
        // Proves spans were really captured — without this the sweep below would pass just
        // as happily on an empty trace.
        Assert.NotEmpty(DatabaseSpans());
        Assert.False(string.IsNullOrWhiteSpace(_storedCiphertext));

        foreach (var attribute in SpanAttributes())
        {
            Assert.DoesNotContain(SentinelPassword, attribute, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_storedCiphertext, attribute, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_span_attribute_ever_carries_the_encryption_key()
    {
        Assert.NotEmpty(DatabaseSpans());

        foreach (var attribute in SpanAttributes())
        {
            Assert.DoesNotContain(
                TestWebApplicationFactory.TestEncryptionKey,
                attribute,
                StringComparison.Ordinal
            );
        }
    }
}
