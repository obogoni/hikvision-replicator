using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What every device-endpoint test class needs: the running application, a clean registry, and
/// the few readings that have to come from the database rather than from the API — because a
/// promise about what is stored cannot be proved by asking the thing that stores it.
/// <para>
/// The mirror of <see cref="UserApiTests"/>, extracted when AD-037 split the one
/// <c>DeviceEndpointsTests</c> class into one class per use case.
/// </para>
/// </summary>
public abstract class DeviceApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Distinctive enough that a substring search proves a leak rather than coincidence — see
    /// <see cref="CredentialLeakageTests"/>, which sweeps for this exact value (DEV-07).
    /// </summary>
    protected const string SentinelPassword = "s3cr3t-Passw0rd";

    protected PostgresFixture Fixture { get; } = fixture;

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected static object ValidRegistration(
        string ipAddress = "192.168.1.10",
        int httpPort = 80,
        string name = "Front Gate Reader",
        string username = "admin",
        string password = SentinelPassword,
        int faceCapacity = 10_000
    ) =>
        new
        {
            name,
            ipAddress,
            httpPort,
            username,
            password,
            faceCapacity,
        };

    protected Task<HttpResponseMessage> RegisterAsync(object request) =>
        Client.PostAsJsonAsync("/api/devices", request);

    protected Task<HttpResponseMessage> UpdateAsync(int id, object request) =>
        Client.PutAsJsonAsync($"/api/devices/{id}", request);

    protected static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts a 400 problem body whose validation errors name the given field.</summary>
    protected static async Task AssertRejectedFieldAsync(
        HttpResponseMessage response,
        string expectedField
    )
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadBodyAsync(response);
        var errors = body.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty(expectedField, out var messages),
            $"Expected the problem body to name '{expectedField}'. Body: {body}"
        );
        Assert.NotEmpty(messages.EnumerateArray());
    }

    protected static async Task<Dictionary<string, string>> ProblemFieldsAsync(
        HttpResponseMessage response
    ) =>
        (await ReadBodyAsync(response))
            .EnumerateObject()
            .Where(property => property.Name != "traceId")
            .ToDictionary(property => property.Name, property => property.Value.ToString());

    /// <summary>
    /// The backfill debts as the database holds them. Nothing returns an intent over HTTP —
    /// there is no queue API in this feature — so what was recorded is read where it lives.
    /// </summary>
    protected async Task<List<BackfillIntent>> BackfillDebtsAsync()
    {
        await using var db = Fixture.CreateDbContext();
        return await db.BackfillIntents.OrderBy(intent => intent.Id).ToListAsync();
    }

    protected async Task<int> CountQueuedWorkAsync()
    {
        await using var db = Fixture.CreateDbContext();
        return await db.Replications.CountAsync();
    }

    protected async Task<List<Replication>> QueuedWorkAsync()
    {
        await using var db = Fixture.CreateDbContext();
        return await db.Replications.OrderBy(work => work.Id).ToListAsync();
    }

    /// <summary>
    /// Registers a spectator through its own route, so the live fan-out puts real work in
    /// the queue for every reader already in the catalogue.
    /// </summary>
    protected async Task UpsertSpectatorAsync(string externalRef, string accessCode)
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/users/{Uri.EscapeDataString(externalRef)}",
            new
            {
                name = "Ada Lovelace",
                accessCode,
                facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    /// <summary>
    /// Seeds spectators straight into the registry. Registration through the route would put
    /// a face picture through normalization for each one, which is minutes for the roster
    /// sizes REP-14 is about — and the arrangement here is only ever "there are N of them".
    /// </summary>
    protected async Task GivenActiveSpectatorsAsync(int count)
    {
        await using var db = Fixture.CreateDbContext();

        for (var index = 0; index < count; index++)
        {
            // A fingerprint of its own per spectator: it is owned by the user row, so one
            // shared instance cannot be written fifty times.
            db.Users.Add(
                User.Create(
                        $"SEEDED-{index}",
                        "Ada Lovelace",
                        $"{100_000 + index}",
                        FaceFingerprint.Create($"0f1e2d{index:x2}", 51_200, 800, 600).AsT0,
                        [0x01, 0x02, 0x03],
                        SeededAt
                    )
                    .AsT0
            );
        }

        await db.SaveChangesAsync();
    }

    private static readonly DateTime SeededAt = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    protected async Task<int> CountDevicesAsync()
    {
        await using var db = Fixture.CreateDbContext();
        return await db.Devices.CountAsync();
    }

    /// <summary>Registers a device and returns it as the database reports it.</summary>
    protected async Task<(int Id, JsonElement Device)> GivenRegisteredDeviceAsync(
        string ipAddress = "192.168.1.10",
        string name = "Front Gate Reader",
        string password = SentinelPassword
    )
    {
        var registration = await RegisterAsync(
            ValidRegistration(ipAddress: ipAddress, name: name, password: password)
        );
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var id = (await ReadBodyAsync(registration)).GetProperty("id").GetInt32();

        // Read back through the API so every timestamp comparison uses the value the
        // database stores, not the finer-grained in-memory one.
        return (id, await ReadBodyAsync(await Client.GetAsync($"/api/devices/{id}")));
    }

    /// <summary>The ciphertext as stored, which the API deliberately never returns (DEV-07).</summary>
    protected async Task<string> ReadStoredPasswordAsync(int id)
    {
        await using var db = Fixture.CreateDbContext();
        return (await db.Devices.SingleAsync(device => device.Id == id)).EncryptedPassword;
    }
}
