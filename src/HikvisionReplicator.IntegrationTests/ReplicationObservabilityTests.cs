using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What the queue tells an operator while it fills. REP-24 and REP-25: a spectator whose
/// arrival takes the roster past a reader's ceiling is <b>accepted anyway</b>, and the reader
/// is named in a warning instead.
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

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await fixture.ResetAsync();

        // A sink per test class, on a host of its own: the lines another class provokes never
        // reach it, so "exactly one warning" is a claim about this test's traffic.
        _factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureLogging(logging => logging.AddProvider(_logSink))
        );
        _client = _factory.CreateClient();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _client?.Dispose();
        _factory?.Dispose();
        _logSink.Dispose();
    }

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
    private Task<HttpResponseMessage> ArriveAsync(int ticket) =>
        _client.PutAsJsonAsync(
            $"/api/users/TICKET-{ticket}",
            new
            {
                name = "Ada Lovelace",
                accessCode = $"77880{ticket}",
                facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
            }
        );
}
