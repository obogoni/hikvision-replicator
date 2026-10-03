using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Events;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The save hook, as a cross-cutting concern rather than as a use case: what an aggregate
/// raised reaches its handlers <em>inside</em> the write that raised it, and the write and
/// everything the handlers staged stand or fall together (AD-042, REP-07).
/// <para>
/// Most of these drive a real request and read the tables afterwards. The last two do not,
/// and each carries the sentence AD-036 demands of a test that goes below HTTP — AD-040
/// records that the list of qualifying classes is a snapshot, not a cap. They are here and
/// not in <see cref="ReplicationQueueContractTests"/> because what they assert is the
/// behaviour of the save, not of the queue.
/// </para>
/// <para>
/// No real handler exists yet, so the handlers below are the fakes that let the ordering be
/// asserted on its own, before any rule depends on it.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class DomainEventDispatchTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // ─── The fakes ───────────────────────────────────────────────────────

    /// <summary>Records which events reached a handler, across the host's own threads.</summary>
    private sealed class InvocationLog
    {
        private readonly ConcurrentQueue<string> _handled = new();

        public void Record(string eventName) => _handled.Enqueue(eventName);

        public IReadOnlyList<string> Handled => [.. _handled];
    }

    private sealed class CountingHandler<TEvent>(InvocationLog log) : IDomainEventHandler<TEvent>
        where TEvent : IDomainEvent
    {
        public Task HandleAsync(TEvent domainEvent, CancellationToken cancellationToken)
        {
            log.Record(typeof(TEvent).Name);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Stands in for the fan-out: stages work for the spectator the event announced, against
    /// every reader in the catalogue, and <b>never saves</b> (AD-041).
    /// </summary>
    private sealed class StagingHandler(AppDbContext context)
        : IDomainEventHandler<UserRegistered>
    {
        public async Task HandleAsync(
            UserRegistered domainEvent,
            CancellationToken cancellationToken
        )
        {
            var deviceIds = await context
                .Devices.Select(device => device.Id)
                .ToListAsync(cancellationToken);

            foreach (var deviceId in deviceIds)
            {
                context.Replications.Add(
                    Replication.Create(
                        domainEvent.User,
                        deviceId,
                        ReplicationOperation.Add,
                        ReplicationLane.Live,
                        domainEvent.OccurredAt
                    )
                );
            }
        }
    }

    private sealed class ThrowingHandler : IDomainEventHandler<UserRegistered>
    {
        public const string Refusal = "the handler refused this write";

        public Task HandleAsync(UserRegistered domainEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException(Refusal);
    }

    // ─── The harness ─────────────────────────────────────────────────────

    private WebApplicationFactory<Program> HostWith(Action<IServiceCollection> register) =>
        fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
            {
                services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
                register(services);
            })
        );

    private static object ValidUpsert(string name = "Ada Lovelace") =>
        new
        {
            name,
            accessCode = "123456",
            facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait),
        };

    private static object ValidRegistration(string ipAddress = "10.0.0.5") =>
        new
        {
            name = "Turnstile A",
            ipAddress,
            httpPort = 80,
            username = "admin",
            password = "s3cr3t-Passw0rd",
            faceCapacity = 50_000,
        };

    private static Task<HttpResponseMessage> UpsertAsync(HttpClient client, string externalRef) =>
        client.PutAsJsonAsync($"/api/users/{Uri.EscapeDataString(externalRef)}", ValidUpsert());

    private async Task<int> RegisterDeviceAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/devices", ValidRegistration());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<DeviceIdentity>())!.Id;
    }

    private sealed record DeviceIdentity(int Id);

    private async Task<List<Replication>> StoredWorkAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.Replications.ToListAsync();
    }

    private async Task<int> CountUsersAsync()
    {
        await using var context = fixture.CreateDbContext();
        return await context.Users.CountAsync();
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        await using var context = fixture.CreateDbContext();
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static User NewUser(string externalRef) =>
        User.Create(
                externalRef,
                "Ada Lovelace",
                "123456",
                FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
                [0x01, 0x02, 0x03],
                Now
            )
            .AsT0;

    // ─── Dispatch runs before the save, so staged rows join it (REP-07) ──

    [Fact]
    public async Task Work_staged_by_a_handler_is_committed_by_the_write_that_raised_the_event()
    {
        var log = new InvocationLog();
        using var factory = HostWith(services =>
        {
            services.AddSingleton(log);
            services.AddScoped<IDomainEventHandler<UserRegistered>, StagingHandler>();
        });
        using var client = factory.CreateClient();
        var deviceId = await RegisterDeviceAsync(client);

        var response = await UpsertAsync(client, "TICKET-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountUsersAsync());

        // Had dispatch run after base.SaveChangesAsync, this row would have been staged into
        // a save that never happened and the table would be empty.
        var staged = Assert.Single(await StoredWorkAsync());
        Assert.Equal(deviceId, staged.DeviceId);
        Assert.NotEqual(0, staged.UserId);
        Assert.Equal(ReplicationStatus.Pending, staged.Status);
    }

    [Fact]
    public async Task A_handler_that_fails_leaves_the_write_that_raised_it_uncommitted()
    {
        using var factory = HostWith(services =>
            services.AddScoped<IDomainEventHandler<UserRegistered>, ThrowingHandler>()
        );
        using var client = factory.CreateClient();

        var response = await UpsertAsync(client, "TICKET-1");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task A_write_that_fails_commits_nothing_a_handler_staged()
    {
        using var factory = HostWith(services =>
            services.AddScoped<IDomainEventHandler<UserRegistered>, StagingHandler>()
        );
        using var client = factory.CreateClient();
        await RegisterDeviceAsync(client);

        // Refuses the second half of the user write at the database, which is the only way to
        // make the save fail after the handler has already staged its rows into it.
        await ExecuteSqlAsync(
            """ALTER TABLE face_pictures ADD CONSTRAINT refuse_writes CHECK (false)"""
        );
        try
        {
            var response = await UpsertAsync(client, "TICKET-1");

            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(0, await CountUsersAsync());
            Assert.Empty(await StoredWorkAsync());
        }
        finally
        {
            await ExecuteSqlAsync("""ALTER TABLE face_pictures DROP CONSTRAINT refuse_writes""");
        }
    }

    [Fact]
    public async Task A_write_that_changes_nothing_reaches_no_handler()
    {
        var log = new InvocationLog();
        using var factory = HostWith(services =>
        {
            services.AddSingleton(log);
            services.AddScoped<IDomainEventHandler<UserRegistered>, CountingHandler<UserRegistered>>();
            services.AddScoped<IDomainEventHandler<UserChanged>, CountingHandler<UserChanged>>();
        });
        using var client = factory.CreateClient();

        await UpsertAsync(client, "TICKET-1");
        var afterRegistration = log.Handled;

        // Byte-identical, so the aggregate raises nothing — and the save still happens.
        var repeat = await UpsertAsync(client, "TICKET-1");

        Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
        Assert.Equal([nameof(UserRegistered)], afterRegistration);
        Assert.Equal([nameof(UserRegistered)], log.Handled);
    }

    // ─── Events are cleared only once the save has returned ──────────────

    /// <summary>
    /// That a save which threw leaves the aggregate still carrying what it raised.
    /// <para>
    /// HTTP cannot distinguish it: the caller gets the same failure either way, and the
    /// difference only surfaces on a later save of the same tracked instance — which no
    /// single request performs. Clearing the events before <c>base</c> would look identical
    /// from outside and would silently throw away the fan-out a retry depends on.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_failed_write_leaves_its_aggregate_still_carrying_the_events_it_raised()
    {
        await ExecuteSqlAsync("""ALTER TABLE users ADD CONSTRAINT refuse_writes CHECK (false)""");
        try
        {
            await using var context = fixture.CreateDbContext();
            var user = NewUser("TICKET-1");
            context.Users.Add(user);

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());

            Assert.Contains(user.DomainEvents, raised => raised is UserRegistered);
        }
        finally
        {
            await ExecuteSqlAsync("""ALTER TABLE users DROP CONSTRAINT refuse_writes""");
        }
    }

    /// <summary>
    /// That a save which committed leaves the aggregate carrying nothing.
    /// <para>
    /// HTTP cannot distinguish it either: the response is the same whether or not the
    /// aggregate still holds the event. Never clearing would satisfy the failure test above
    /// and would fan the same spectator out again on the next save of that instance.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_committed_write_leaves_its_aggregate_carrying_nothing()
    {
        await using var context = fixture.CreateDbContext();
        var user = NewUser("TICKET-1");
        context.Users.Add(user);

        await context.SaveChangesAsync();

        Assert.Empty(user.DomainEvents);
    }
}
