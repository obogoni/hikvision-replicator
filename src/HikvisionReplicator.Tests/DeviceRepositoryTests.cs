using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Tests;

/// <summary>
/// The database is the authority for address uniqueness (AD-022, DEV-06), so these tests
/// drive the repository directly — deliberately bypassing any service-level pre-check —
/// and assert the constraint violation arrives as a domain error, not an exception.
/// </summary>
[Collection(PostgresCollection.Name)]
public class DeviceRepositoryTests(PostgresFixture fixture) : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static Device NewDevice(string ipAddress, int httpPort = 80) =>
        Device.Create("Front Gate Reader", ipAddress, httpPort, "admin", "IV:cipher", 10_000, Now)
            .AsT0;

    private async Task<int> GivenRegisteredDeviceAsync(string ipAddress, int httpPort = 80)
    {
        await using var context = fixture.CreateDbContext();
        var device = NewDevice(ipAddress, httpPort);
        context.Devices.Add(device);
        await context.SaveChangesAsync();
        return device.Id;
    }

    private async Task<Device> ReadDeviceAsync(int id)
    {
        await using var context = fixture.CreateDbContext();
        return await context.Devices.SingleAsync(device => device.Id == id);
    }

    // ─── DEV-05 / DEV-06: the insert path ────────────────────────────────

    [Fact]
    public async Task Device_with_a_free_address_is_stored()
    {
        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);

        var result = await repository.AddIfAddressFreeAsync(
            NewDevice("192.168.1.10"),
            CancellationToken.None
        );

        Assert.True(result.IsT0);

        await using var verification = fixture.CreateDbContext();
        var stored = await verification.Devices.SingleAsync();
        Assert.Equal("192.168.1.10", stored.IpAddress.Value);
        Assert.Equal(80, stored.HttpPort.Value);
    }

    [Fact]
    public async Task Device_reusing_a_registered_address_is_rejected_as_a_conflict()
    {
        await GivenRegisteredDeviceAsync("192.168.1.10");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);

        var result = await repository.AddIfAddressFreeAsync(
            NewDevice("192.168.1.10"),
            CancellationToken.None
        );

        Assert.True(result.IsT1);
        Assert.Equal(IDeviceRepository.AddressAlreadyRegistered, result.AsT1.Message);

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(1, await verification.Devices.CountAsync());
    }

    // ─── DEV-20: the update path ─────────────────────────────────────────

    [Fact]
    public async Task Address_change_onto_another_devices_address_is_rejected_as_a_conflict()
    {
        await GivenRegisteredDeviceAsync("192.168.1.10");
        var movingDeviceId = await GivenRegisteredDeviceAsync("192.168.1.11");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);
        var device = await context.Devices.SingleAsync(d => d.Id == movingDeviceId);
        device.Update(null, "192.168.1.10", null, null, null, null, Now.AddMinutes(1));

        var result = await repository.SaveIfAddressFreeAsync(CancellationToken.None);

        Assert.True(result.IsT1);

        var persisted = await ReadDeviceAsync(movingDeviceId);
        Assert.Equal("192.168.1.11", persisted.IpAddress.Value);
    }

    [Fact]
    public async Task Address_change_onto_a_free_address_is_stored()
    {
        var deviceId = await GivenRegisteredDeviceAsync("192.168.1.11");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);
        var device = await context.Devices.SingleAsync(d => d.Id == deviceId);
        device.Update(null, "192.168.1.99", null, null, null, null, Now.AddMinutes(1));

        var result = await repository.SaveIfAddressFreeAsync(CancellationToken.None);

        Assert.True(result.IsT0);

        var persisted = await ReadDeviceAsync(deviceId);
        Assert.Equal("192.168.1.99", persisted.IpAddress.Value);
    }

    // ─── AD-022: only the address constraint is translated ───────────────

    [Fact]
    public async Task Failure_that_is_not_an_address_collision_is_not_reported_as_a_conflict()
    {
        var deviceId = await GivenRegisteredDeviceAsync("192.168.1.10");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);
        var device = await context.Devices.SingleAsync(d => d.Id == deviceId);

        // Remove the row behind the tracked instance, so saving fails for a reason that
        // has nothing to do with the address index.
        await using (var other = fixture.CreateDbContext())
        {
            await other.Devices.Where(d => d.Id == deviceId).ExecuteDeleteAsync();
        }

        device.Update("Renamed Reader", null, null, null, null, null, Now.AddMinutes(1));

        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            repository.SaveIfAddressFreeAsync(CancellationToken.None)
        );
    }

    // ─── DEV-05 / DEV-20: the address specifications ─────────────────────

    [Fact]
    public async Task Device_holding_an_address_is_found_by_that_address()
    {
        var deviceId = await GivenRegisteredDeviceAsync("192.168.1.10");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);

        var found = await repository.FirstOrDefaultAsync(
            new DeviceByAddressSpec(IpAddress.Create("192.168.1.10").AsT0, Port.Create(80).AsT0),
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Equal(deviceId, found.Id);
    }

    [Fact]
    public async Task No_device_is_found_at_an_unclaimed_address()
    {
        await GivenRegisteredDeviceAsync("192.168.1.10");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);

        var found = await repository.FirstOrDefaultAsync(
            new DeviceByAddressSpec(IpAddress.Create("192.168.1.99").AsT0, Port.Create(80).AsT0),
            CancellationToken.None
        );

        Assert.Null(found);
    }

    [Fact]
    public async Task Another_device_holding_the_address_is_found_when_excluding_the_device_being_updated()
    {
        var holderId = await GivenRegisteredDeviceAsync("192.168.1.10");
        var movingDeviceId = await GivenRegisteredDeviceAsync("192.168.1.11");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);

        var found = await repository.FirstOrDefaultAsync(
            new DeviceByAddressExcludingSpec(
                IpAddress.Create("192.168.1.10").AsT0,
                Port.Create(80).AsT0,
                movingDeviceId
            ),
            CancellationToken.None
        );

        Assert.NotNull(found);
        Assert.Equal(holderId, found.Id);
    }

    [Fact]
    public async Task Device_is_never_matched_by_its_own_address_when_it_is_the_one_excluded()
    {
        var deviceId = await GivenRegisteredDeviceAsync("192.168.1.10");

        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);

        var found = await repository.FirstOrDefaultAsync(
            new DeviceByAddressExcludingSpec(
                IpAddress.Create("192.168.1.10").AsT0,
                Port.Create(80).AsT0,
                deviceId
            ),
            CancellationToken.None
        );

        Assert.Null(found);
    }
}
