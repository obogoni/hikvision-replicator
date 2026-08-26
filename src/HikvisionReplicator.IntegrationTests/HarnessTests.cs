using HikvisionReplicator.Api.Domain;

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
}
