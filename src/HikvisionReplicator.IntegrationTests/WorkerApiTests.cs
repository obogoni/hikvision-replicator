using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What every worker test needs: the running application, a clean queue and fleet, and the
/// readings that prove what the worker did — the queue as the database holds it and the calls
/// the fake readers received. Nothing returns a replication over HTTP (feature 8 owns that), so
/// both are read where they live (AD-036).
/// <para>
/// Work is driven through the routes wherever a route can produce it. Where it cannot — a
/// lane HTTP never stages, an ordering that needs chosen timestamps — rows are arranged by
/// hand inside one transaction, so the worker sees all of them or none.
/// </para>
/// </summary>
public abstract class WorkerApiTests(WorkerPostgresFixture fixture) : IAsyncLifetime
{
    protected const string DefaultAccessCode = "123456";
    protected const string DevicePassword = "s3cr3t-Passw0rd";

    protected WorkerPostgresFixture Fixture { get; } = fixture;

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    protected FakeDeviceClient Readers => Fixture.Readers;

    protected WorkerClock Clock => Fixture.Clock;

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected static string UserRoute(string externalRef) =>
        $"/api/users/{Uri.EscapeDataString(externalRef)}";

    /// <summary>Registers a reader through its own route and returns its id.</summary>
    protected async Task<int> GivenRegisteredDeviceAsync(string ipAddress)
    {
        var response = await Client.PostAsJsonAsync(
            "/api/devices",
            new
            {
                name = $"Turnstile {ipAddress}",
                ipAddress,
                httpPort = 80,
                username = "admin",
                password = DevicePassword,
                faceCapacity = 50_000,
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await ReadBodyAsync(response)).GetProperty("id").GetInt32();
    }

    protected async Task UpsertSpectatorAsync(
        string externalRef,
        string accessCode = DefaultAccessCode
    )
    {
        var response = await Client.PutAsJsonAsync(
            UserRoute(externalRef),
            new
            {
                name = "Ada Lovelace",
                accessCode,
                facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
            }
        );

        Assert.True(response.IsSuccessStatusCode, $"Upsert answered {response.StatusCode}.");
    }

    protected async Task RemoveSpectatorAsync(string externalRef)
    {
        var response = await Client.DeleteAsync(UserRoute(externalRef));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    protected static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    protected async Task<List<Replication>> QueuedWorkAsync()
    {
        await using var context = Fixture.CreateDbContext();
        return await context.Replications.OrderBy(work => work.Id).ToListAsync();
    }

    /// <summary>Polls the queue until <paramref name="done"/> holds for it.</summary>
    protected Task<List<Replication>> QueueUntilAsync(
        Func<List<Replication>, bool> done,
        string because,
        TimeSpan? within = null
    ) => Eventually.UntilAsync(QueuedWorkAsync, done, because, within);

    protected Task<IReadOnlyList<DeviceCall>> CallsUntilAsync(
        Func<IReadOnlyList<DeviceCall>, bool> done,
        string because,
        TimeSpan? within = null
    ) => Eventually.UntilAsync(() => Task.FromResult(Readers.Calls), done, because, within);

    protected async Task<User> StoredUserAsync(string externalRef)
    {
        await using var context = Fixture.CreateDbContext();
        var reference = ExternalRef.Create(externalRef).AsT0;
        return await context.Users.SingleAsync(user => user.ExternalRef == reference);
    }

    /// <summary>The normalized face as stored — what a reader must be sent (WRK-02).</summary>
    protected async Task<byte[]> StoredPictureAsync(string externalRef)
    {
        var user = await StoredUserAsync(externalRef);
        await using var context = Fixture.CreateDbContext();
        return (
            await context.Set<FacePicture>().SingleAsync(picture => picture.UserId == user.Id)
        ).Content;
    }

    /// <summary>
    /// Arranges state by hand, in one transaction, so the worker sees all of it or none of it —
    /// for the shapes no route produces.
    /// </summary>
    protected async Task ArrangeAsync(Func<AppDbContext, Task> arrange)
    {
        await using var context = Fixture.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await arrange(context);
        await transaction.CommitAsync();
    }

    /// <summary>A reader added straight to the catalogue, with a password the worker can decrypt.</summary>
    protected Device NewReader(string ipAddress)
    {
        var encryption = Fixture.Factory.Services.GetRequiredService<IEncryptionService>();
        return Device
            .Create(
                $"Turnstile {ipAddress}",
                ipAddress,
                80,
                "admin",
                encryption.Encrypt(DevicePassword),
                50_000,
                DateTime.UtcNow
            )
            .AsT0;
    }

    /// <summary>A spectator added straight to the registry, with a face of its own.</summary>
    protected static User NewSpectator(string externalRef, string accessCode) =>
        User.Create(
                externalRef,
                "Ada Lovelace",
                accessCode,
                FaceFingerprint.Create($"0f1e2d{accessCode}", 51_200, 800, 600).AsT0,
                [0xFF, 0xD8, 0xFF],
                DateTime.UtcNow
            )
            .AsT0;
}
