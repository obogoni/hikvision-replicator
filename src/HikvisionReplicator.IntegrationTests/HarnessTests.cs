using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace HikvisionReplicator.IntegrationTests;

[Collection(PostgresCollection.Name)]
public class HarnessTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private async Task<int> CountDevicesAsync()
    {
        await using var db = fixture.CreateDbContext();
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            db.Devices
        );
    }

    private async Task RegisterDeviceAsync(string ipAddress)
    {
        await using var db = fixture.CreateDbContext();
        var device = Device
            .Create("Front Gate Reader", ipAddress, 80, "admin", "IV:cipher", 10_000, Now)
            .AsT0;
        db.Devices.Add(device);
        await db.SaveChangesAsync();
    }

    // ─── DEV-12: the application creates its own schema at startup ────────

    [Fact]
    public async Task Application_boots_against_an_empty_database_and_creates_its_schema()
    {
        var tables = await fixture.ListPublicTablesAsync();

        Assert.Contains("devices", tables);
        Assert.Contains(PostgresFixture.MigrationHistoryTable, tables);
    }

    [Fact]
    public async Task Schema_is_recorded_as_applied_migrations()
    {
        var applied = await fixture.ListAppliedMigrationsAsync();

        Assert.NotEmpty(applied);
        Assert.Contains(applied, migration => migration.EndsWith("InitialCreate"));

        // Naming only the first migration would stay green with every later one unapplied,
        // which is the state a half-migrated database is actually in (Verifier, AD-036).
        Assert.Contains(
            applied,
            migration => migration.EndsWith("AddUserRegistry", StringComparison.Ordinal)
        );
    }

    // ─── DEV-13: state is isolated between tests ─────────────────────────
    // Both tests insert exactly one device and demand they see only their own.
    // Whichever runs second fails if the reset between tests did not happen.

    [Fact]
    public async Task Each_test_starts_from_an_empty_catalogue()
    {
        Assert.Equal(0, await CountDevicesAsync());

        await RegisterDeviceAsync("192.168.1.10");

        Assert.Equal(1, await CountDevicesAsync());
    }

    [Fact]
    public async Task Devices_written_by_another_test_are_not_visible()
    {
        Assert.Equal(0, await CountDevicesAsync());

        await RegisterDeviceAsync("192.168.1.10");

        Assert.Equal(1, await CountDevicesAsync());
    }

    // ─── The queue's own schema, created the same way (REP-09, REP-14, REP-16) ────

    [Fact]
    public async Task Queue_tables_are_created_at_startup_alongside_the_catalogues()
    {
        var tables = await fixture.ListPublicTablesAsync();

        Assert.Contains(ReplicationConfiguration.TableName, tables);
        Assert.Contains(BackfillIntentConfiguration.TableName, tables);
    }

    [Fact]
    public async Task Queue_schema_is_recorded_as_applied_migrations()
    {
        var applied = await fixture.ListAppliedMigrationsAsync();

        Assert.Contains(
            applied,
            migration => migration.EndsWith("AddReplicationQueue", StringComparison.Ordinal)
        );
        Assert.Contains(
            applied,
            migration => migration.EndsWith("AddBackfillIntents", StringComparison.Ordinal)
        );
    }

    /// <summary>
    /// The mapping and the migrations describe the same schema. Without this, a configuration
    /// change with no migration behind it ships a model the database has never been told
    /// about — and the only symptom is a runtime failure on the first query that needs the
    /// column.
    /// </summary>
    [Fact]
    public void Schema_the_application_maps_is_the_schema_its_migrations_build()
    {
        using var context = fixture.CreateDbContext();

        Assert.False(context.Database.HasPendingModelChanges());
    }

    /// <summary>
    /// The upgrade path, not the greenfield one. Every other test here starts from an empty
    /// database, where a migration that silently depends on an empty table cannot be caught;
    /// a deployed instance already holds spectators and readers, and the queue's two foreign
    /// keys point straight at them.
    /// </summary>
    [Fact]
    public async Task Queue_tables_are_added_to_a_database_that_already_holds_spectators_and_readers()
    {
        const string ProbeDatabase = "replication_queue_upgrade_probe";
        const string LastCatalogueMigration = "AddUserRegistry";

        await RunOnServerAsync($"""DROP DATABASE IF EXISTS "{ProbeDatabase}" WITH (FORCE)""");
        await RunOnServerAsync($"""CREATE DATABASE "{ProbeDatabase}" """);

        try
        {
            var options = ProbeOptions(ProbeDatabase);
            int userId;
            int deviceId;

            await using (var catalogueOnly = new AppDbContext(options))
            {
                // Stop at the last migration before the queue existed: the state a deployed
                // instance is in on the day this feature ships.
                await catalogueOnly.GetService<IMigrator>().MigrateAsync(LastCatalogueMigration);

                var user = User
                    .Create(
                        "TICKET-1",
                        "Ada Lovelace",
                        "123456",
                        FaceFingerprint.Create("0f1e2d3c", 51_200, 800, 600).AsT0,
                        [0x01, 0x02, 0x03],
                        Now
                    )
                    .AsT0;
                var device = Device
                    .Create("Turnstile A", "10.0.0.5", 80, "admin", "cipher", 50_000, Now)
                    .AsT0;

                catalogueOnly.Users.Add(user);
                catalogueOnly.Devices.Add(device);
                await catalogueOnly.SaveChangesAsync();

                userId = user.Id;
                deviceId = device.Id;
            }

            await using (var upgraded = new AppDbContext(options))
            {
                await upgraded.Database.MigrateAsync();
            }

            await using (var verification = new AppDbContext(options))
            {
                // The rows that were already there survived the upgrade …
                Assert.Equal(1, await verification.Users.CountAsync());
                Assert.Equal(1, await verification.Devices.CountAsync());

                // … and the queue can now reference them, which is the only thing that
                // proves the foreign keys were built against the live catalogue tables.
                verification.Replications.Add(
                    Replication.Create(
                        userId,
                        deviceId,
                        ReplicationOperation.Add,
                        ReplicationLane.Live,
                        Now
                    )
                );
                verification.BackfillIntents.Add(BackfillIntent.Create(deviceId, Now));
                await verification.SaveChangesAsync();

                Assert.Equal(1, await verification.Replications.CountAsync());
                Assert.Equal(1, await verification.BackfillIntents.CountAsync());
            }
        }
        finally
        {
            await RunOnServerAsync($"""DROP DATABASE IF EXISTS "{ProbeDatabase}" WITH (FORCE)""");
        }
    }

    private async Task RunOnServerAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private DbContextOptions<AppDbContext> ProbeOptions(string database) =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                new NpgsqlConnectionStringBuilder(fixture.ConnectionString)
                {
                    Database = database,
                }.ConnectionString
            )
            // The probe deliberately rests at an older migration for part of its life, which
            // is exactly the state this warning exists to refuse. Suppressed here and nowhere
            // else — `Schema_the_application_maps_is_the_schema_its_migrations_build` is what
            // keeps the real context honest.
            .ConfigureWarnings(warnings =>
                warnings.Ignore(RelationalEventId.PendingModelChangesWarning)
            )
            .Options;
}
