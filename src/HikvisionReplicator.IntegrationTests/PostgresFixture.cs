using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using Respawn.Graph;
using Testcontainers.PostgreSql;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// One PostgreSQL container per test collection (AD-019). The schema is created by the
/// application's own startup migration run, so the harness never diverges from the way
/// production builds its database (DEV-12). Respawn clears the data between tests so no
/// test observes another's rows (DEV-13).
/// </summary>
public class PostgresFixture : IAsyncLifetime
{
    public const string MigrationHistoryTable = "__EFMigrationsHistory";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private Respawner _respawner = null!;
    private NpgsqlConnection _connection = null!;

    public string ConnectionString => _container.GetConnectionString();

    public TestWebApplicationFactory Factory { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Factory = new TestWebApplicationFactory(ConnectionString);

        // Starting the host runs the application's own Migrate() against the empty
        // database — the behaviour DEV-12 describes.
        using (var _ = Factory.CreateClient()) { }

        _connection = new NpgsqlConnection(ConnectionString);
        await _connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(
            _connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = [new Table(MigrationHistoryTable)],
            }
        );
    }

    public Task ResetAsync() => _respawner.ResetAsync(_connection);

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    public async Task<IReadOnlyList<string>> ListPublicTablesAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText =
            "SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'";

        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));

        return tables;
    }

    public async Task<IReadOnlyList<string>> ListAppliedMigrationsAsync()
    {
        await using var command = _connection.CreateCommand();
        command.CommandText = $"""SELECT "MigrationId" FROM "{MigrationHistoryTable}" """;

        var migrations = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            migrations.Add(reader.GetString(0));

        return migrations;
    }

    public async Task DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
        Factory?.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
