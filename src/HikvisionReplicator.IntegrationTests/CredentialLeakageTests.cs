using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Infrastructure.Isapi;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>Keeps every line the application logs, verbatim, for inspection.</summary>
public sealed class InMemoryLogSink : ILoggerProvider
{
    private readonly List<string> _lines = [];

    public IReadOnlyList<string> Lines
    {
        get
        {
            lock (_lines)
                return [.. _lines];
        }
    }

    public ILogger CreateLogger(string categoryName) => new SinkLogger(this, categoryName);

    public void Dispose() { }

    private void Write(string line)
    {
        lock (_lines)
            _lines.Add(line);
    }

    private sealed class SinkLogger(InMemoryLogSink sink, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => sink.Write($"{category} [{logLevel}] {formatter(state, exception)} {exception}");
    }
}

/// <summary>
/// DEV-07 end to end: a device password is stored encrypted and never surfaces — not in a
/// response body, not in a log line — no matter which route touched it. Every device route
/// is driven once with a distinctive sentinel, and everything the application emitted while
/// doing so is then inspected.
/// </summary>
[Collection(PostgresCollection.Name)]
public class CredentialLeakageTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string SentinelPassword = "Sentinel-P@ssw0rd-8f3a1c92";
    private const string ReplacementPassword = "Sentinel-Replaced-4b7e0d51";

    private readonly InMemoryLogSink _logSink = new();
    private readonly List<string> _responseBodies = [];

    private WebApplicationFactory<Program> _factory = null!;
    private string _storedCiphertext = string.Empty;
    private string _replacedCiphertext = string.Empty;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        _factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            // The configured filters would otherwise drop the EF Core command logs, which
            // are the ones that carry SQL and its parameters.
            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["Logging:LogLevel:Default"] = "Trace",
                        ["Logging:LogLevel:Microsoft"] = "Trace",
                        ["Logging:LogLevel:Microsoft.EntityFrameworkCore"] = "Trace",
                    }
                )
            );

            builder.ConfigureLogging(logging =>
            {
                logging.SetMinimumLevel(LogLevel.Trace);
                logging.AddProvider(_logSink);
            });
        });

        using var client = _factory.CreateClient();
        await ExerciseEveryDeviceRouteAsync(client);
    }

    public Task DisposeAsync()
    {
        _factory?.Dispose();
        return Task.CompletedTask;
    }

    private async Task ExerciseEveryDeviceRouteAsync(HttpClient client)
    {
        var registration = await client.PostAsJsonAsync(
            "/api/devices",
            new
            {
                name = "Front Gate Reader",
                ipAddress = "192.168.9.10",
                httpPort = 80,
                username = "admin",
                password = SentinelPassword,
                faceCapacity = 10_000,
            }
        );
        var registrationBody = await CaptureAsync(registration);
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        using var document = JsonDocument.Parse(registrationBody);
        var id = document.RootElement.GetProperty("id").GetInt32();
        _storedCiphertext = await ReadStoredPasswordAsync(id);

        await CaptureAsync(await client.GetAsync($"/api/devices/{id}"));
        await CaptureAsync(await client.GetAsync("/api/devices"));

        var update = await client.PutAsJsonAsync(
            $"/api/devices/{id}",
            new { password = ReplacementPassword }
        );
        await CaptureAsync(update);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        _replacedCiphertext = await ReadStoredPasswordAsync(id);

        var removal = await client.DeleteAsync($"/api/devices/{id}");
        await CaptureAsync(removal);
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
    }

    private async Task<string> CaptureAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        _responseBodies.Add(body);
        return body;
    }

    private async Task<string> ReadStoredPasswordAsync(int id)
    {
        await using var db = fixture.CreateDbContext();
        return await db
            .Devices.Where(device => device.Id == id)
            .Select(device => device.EncryptedPassword)
            .SingleAsync();
    }

    private static EncryptionService TestEncryptionService() =>
        new(
            Options.Create(
                new EncryptionOptions { Key = TestWebApplicationFactory.TestEncryptionKey }
            )
        );

    // ─── DEV-07: nothing the caller receives carries the credential ──────

    [Fact]
    public void No_device_response_ever_carries_the_password()
    {
        Assert.Equal(5, _responseBodies.Count);

        foreach (var body in _responseBodies)
        {
            Assert.DoesNotContain(SentinelPassword, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ReplacementPassword, body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_storedCiphertext, body, StringComparison.Ordinal);
            Assert.DoesNotContain(_replacedCiphertext, body, StringComparison.Ordinal);
        }
    }

    // ─── DEV-07: nothing the application logs carries the credential ─────

    [Fact]
    public void No_log_line_ever_carries_the_password()
    {
        // Proves the sink really sees the application's own logging, including the EF Core
        // command logs — without this the sweep below could pass on an empty sink.
        Assert.Contains(_logSink.Lines, line => line.Contains("Executed DbCommand"));

        foreach (var line in _logSink.Lines)
        {
            Assert.DoesNotContain(SentinelPassword, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(ReplacementPassword, line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(_storedCiphertext, line, StringComparison.Ordinal);
            Assert.DoesNotContain(_replacedCiphertext, line, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void No_log_line_ever_carries_the_encryption_key()
    {
        Assert.NotEmpty(_logSink.Lines);

        foreach (var line in _logSink.Lines)
        {
            Assert.DoesNotContain(
                TestWebApplicationFactory.TestEncryptionKey,
                line,
                StringComparison.Ordinal
            );
        }
    }

    // ─── DEV-07: what is stored is recoverable ciphertext, not the plaintext ─

    [Fact]
    public void Stored_password_is_ciphertext_that_recovers_the_original()
    {
        var encryptionService = TestEncryptionService();

        Assert.NotEqual(SentinelPassword, _storedCiphertext);
        Assert.NotEqual(ReplacementPassword, _replacedCiphertext);
        Assert.False(string.IsNullOrWhiteSpace(_storedCiphertext));
        Assert.False(string.IsNullOrWhiteSpace(_replacedCiphertext));

        Assert.Equal(SentinelPassword, encryptionService.Decrypt(_storedCiphertext));
        Assert.Equal(ReplacementPassword, encryptionService.Decrypt(_replacedCiphertext));
    }
}

/// <summary>
/// ISA-24 and ISA-25, end to end: a call to a reader never puts the device password, the PIN or
/// the digest <c>Authorization</c> value into a log line, an exported span or a queue row's
/// <c>LastError</c> — whether the call is delivered, refused or refused its credentials.
/// <para>
/// A class of its own beside <see cref="CredentialLeakageTests"/> rather than more tests in it:
/// these flows need the worker running, and the worker runs only on
/// <see cref="WorkerPostgresFixture"/> (test-patterns, "The worker runs in one place only").
/// Every line and every span is swept, not only the adapter's, because a leak is a leak
/// wherever it surfaces.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class ReaderCredentialLeakageTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private const string WrongPassword = "Wrong-Sentinel-Passw0rd-5a2f";

    /// <summary>Twenty digits: the longest PIN allowed, and nothing a log could hold by chance.</summary>
    private const string SentinelPin = "73916482057316948205";

    private readonly InMemorySpanSink _spans = new();

    private Task<IsapiBench> StartBenchAsync(string devicePassword = IsapiBench.ReaderPassword) =>
        IsapiBench.StartAsync(
            Fixture,
            devicePassword,
            new Dictionary<string, string?>
            {
                ["Logging:LogLevel:Default"] = "Trace",
                ["Logging:LogLevel:Microsoft"] = "Trace",
                ["Logging:LogLevel:Microsoft.AspNetCore"] = "Trace",
            },
            builder =>
            {
                // Host settings: the only layer early enough for the endpoint Program.cs reads
                // while assembling the builder (see TracingTests).
                builder.UseSetting("OpenTelemetry:OtlpEndpoint", "http://localhost:4317");
                builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Trace));
                builder.ConfigureServices(services =>
                    services.ConfigureOpenTelemetryTracerProvider(tracing =>
                        tracing.AddInMemoryExporter(_spans)
                    )
                );
            }
        );

    /// <summary>
    /// Every surface swept for every secret. The guards first prove each surface really saw the
    /// call, so the sweep cannot pass on an empty sink.
    /// </summary>
    private async Task AssertNoSecretSurfacesAsync(IsapiBench bench)
    {
        var authorizations = bench.Reader.Authorizations;
        Assert.NotEmpty(authorizations);

        var adapterSpans = _spans.Spans.Where(span =>
            span.Source.Name == "HikvisionReplicator.Isapi"
        );
        Assert.NotEmpty(adapterSpans);
        Assert.Contains(
            bench.Host.Logs.Lines,
            line => line.Contains($"on device {bench.DeviceId} ended", StringComparison.Ordinal)
        );

        var queue = await bench.QueueAsync();
        Assert.NotEmpty(queue);

        string[] surfaces =
        [
            .. bench.Host.Logs.Lines,
            .. _spans.Spans.Select(span => span.DisplayName),
            .. _spans.Spans.SelectMany(span => span.TagObjects).Select(tag => $"{tag.Key}={tag.Value}"),
            .. queue.Select(work => work.LastError ?? string.Empty),
        ];
        string[] secrets =
        [
            IsapiBench.ReaderPassword,
            WrongPassword,
            SentinelPin,
            "Digest ",
            .. authorizations,
        ];

        foreach (var surface in surfaces)
        {
            foreach (var secret in secrets)
                Assert.DoesNotContain(secret, surface, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task A_delivered_enrolment_leaves_no_credential_on_any_surface()
    {
        await using var bench = await StartBenchAsync();

        await bench.UpsertAsync("TICKET-0001", "Ana Souza", SentinelPin);
        await bench.QueueUntilAsync(
            queue => queue.Any(work => work.Status == ReplicationStatus.Succeeded),
            "the enrolment has been delivered"
        );

        await AssertNoSecretSurfacesAsync(bench);
    }

    [Fact]
    public async Task A_refused_face_leaves_no_credential_on_any_surface()
    {
        await using var bench = await StartBenchAsync();
        bench.Reader.Refuse(IsapiStep.FaceCreate, "modelingFailed");

        await bench.UpsertAsync("TICKET-0001", "Ana Souza", SentinelPin);
        await bench.QueueUntilAsync(
            queue => queue.Any(work => work.Status == ReplicationStatus.Failed),
            "the refused enrolment has been dead-lettered"
        );

        await AssertNoSecretSurfacesAsync(bench);
    }

    [Fact]
    public async Task A_refused_password_leaves_no_credential_on_any_surface()
    {
        await using var bench = await StartBenchAsync(WrongPassword);

        await bench.UpsertAsync("TICKET-0001", "Ana Souza", SentinelPin);
        await Eventually.UntilAsync(
            () => Task.FromResult(bench.Host.Logs.Lines),
            lines => lines.Any(line => line.Contains($"Device {bench.DeviceId} paused", StringComparison.Ordinal)),
            "the reader has been paused"
        );
        Assert.Equal(1, bench.Reader.FailedLogins);

        await AssertNoSecretSurfacesAsync(bench);
    }
}
