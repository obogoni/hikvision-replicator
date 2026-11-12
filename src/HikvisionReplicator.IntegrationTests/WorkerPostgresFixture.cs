using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The worker's own database, host and collection. A sibling of <see cref="PostgresFixture"/>
/// rather than a reuse of it: a running worker drains every queued row it can see, so sharing a
/// database with the tests that assert rows stay <c>Pending</c> would turn them red by timing.
/// <para>
/// One host is shared by the collection, so <see cref="ResetAsync"/> also puts the fake fleet
/// back to healthy, the clock back to real time and the save hook back to doing nothing — no
/// test inherits another's script.
/// </para>
/// </summary>
public sealed class WorkerPostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(
        "postgres:17-alpine"
    ).Build();

    private Respawner _respawner = null!;
    private int _isolatedHosts;

    public string ConnectionString => _container.GetConnectionString();

    public WorkerWebApplicationFactory Factory { get; private set; } = null!;

    /// <summary>The reader fleet the running worker delivers to.</summary>
    public FakeDeviceClient Readers => Factory.Services.GetRequiredService<FakeDeviceClient>();

    public WorkerClock Clock => Factory.Clock;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new WorkerWebApplicationFactory(ConnectionString);

        // Starting the host migrates the database and starts the worker.
        using (var _ = Factory.CreateClient()) { }

        await using var connection = await OpenConnectionAsync();
        _respawner = await Respawner.CreateAsync(
            connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new Table(PostgresFixture.MigrationHistoryTable)],
            }
        );
    }

    public async Task ResetAsync()
    {
        Clock.Thaw();
        Readers.Reset();
        Factory.Saves.Reset();
        await using var connection = await OpenConnectionAsync();
        await _respawner.ResetAsync(connection);
    }

    /// <summary>
    /// A worker host of its own, on a database of its own in the same container, running on
    /// <paramref name="settings"/> layered over the harness's, and anything else
    /// <paramref name="configure"/> does to its builder. For what the shared host cannot
    /// give a test: a setting only that test needs, or a host the test may stop. Its database
    /// is fresh, so no other worker drains it and no reset is owed. The caller disposes it.
    /// </summary>
    public async Task<WorkerWebApplicationFactory> StartIsolatedHostAsync(
        IReadOnlyDictionary<string, string?> settings,
        Action<IWebHostBuilder>? configure = null
    )
    {
        var database = $"worker_isolated_{Interlocked.Increment(ref _isolatedHosts)}";
        await using (var connection = await OpenConnectionAsync())
        {
            await using var create = new NpgsqlCommand(
                $"CREATE DATABASE \"{database}\"",
                connection
            );
            await create.ExecuteNonQueryAsync();
        }

        var host = new WorkerWebApplicationFactory(
            new NpgsqlConnectionStringBuilder(ConnectionString) { Database = database }.ConnectionString,
            settings,
            configure
        );

        // Starting the host migrates its database and starts its worker.
        using (host.CreateClient()) { }

        return host;
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task DisposeAsync()
    {
        Factory?.Dispose();
        await _container.DisposeAsync();
    }
}

/// <summary>
/// <b>Runs on its own, after every parallel collection.</b> Two things here depend on nothing
/// else running in the process: assertions on time (WRK-09 holds a five-second bound, WRK-13 a
/// probe schedule), which a CPU shared with the rest of the suite turns into scheduling luck;
/// and the deployment's metrics provider, which subscribes to its meter <em>by name</em> across
/// the whole process — every test host has one of that name, and a host disposed in another
/// collection mid-test takes the merged stream with it.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class WorkerCollectionDefinition : ICollectionFixture<WorkerPostgresFixture>
{
    public const string Name = "worker";
}

/// <summary>
/// Waits for something the worker will do, by looking until it is so or a deadline passes —
/// never by sleeping a fixed time and hoping. The deadline is generous against the fixture's
/// tens-of-milliseconds intervals, so it bounds a broken worker without pacing a working one.
/// </summary>
internal static class Eventually
{
    public static readonly TimeSpan DefaultDeadline = TimeSpan.FromSeconds(10);

    private static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(25);

    public static async Task<T> UntilAsync<T>(
        Func<Task<T>> read,
        Func<T, bool> done,
        string because,
        TimeSpan? within = null
    )
    {
        var deadline = DateTime.UtcNow + (within ?? DefaultDeadline);

        while (true)
        {
            var value = await read();
            if (done(value))
                return value;

            if (DateTime.UtcNow > deadline)
                Assert.Fail($"Timed out waiting until {because}.");

            await Task.Delay(Interval);
        }
    }
}
