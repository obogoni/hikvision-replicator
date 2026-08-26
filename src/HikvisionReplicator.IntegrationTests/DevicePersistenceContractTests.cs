using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// <b>The exception to the black-box rule, and the whole of it for devices (AD-036).</b>
/// <para>
/// Device behaviour is asserted through the HTTP surface in <see cref="DeviceEndpointsTests"/>.
/// These two are here because HTTP cannot distinguish a right implementation from a wrong one:
/// an untranslated failure and a translated one both leave the caller with a response, and a
/// cancellation racing a live request would assert scheduling luck rather than the abort.
/// </para>
/// <para>
/// See <see cref="UserPersistenceContractTests"/> for the rule a test must satisfy to be added
/// to either class.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class DevicePersistenceContractTests(PostgresFixture fixture) : IAsyncLifetime
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

    // ─── AD-022: the address index maps to the address message ───────────

    /// <summary>
    /// A collision on the address index is reported as the address conflict, proved without a
    /// race. `DeviceEndpointsTests` covers this too, but only through an 8-way concurrent
    /// registration, so it holds when a racer loses the insert and not otherwise — the same
    /// scheduling-dependent guard `docs/test-patterns.md` warns about. AD-036 added the
    /// deterministic version for users and omitted it for devices; the Verifier caught it.
    /// </summary>
    [Fact]
    public async Task Address_collision_is_reported_as_the_address_conflict()
    {
        await GivenRegisteredDeviceAsync("192.168.1.10");

        await using var context = fixture.CreateDbContext();

        var result = await new DeviceRepository(context).AddIfAddressFreeAsync(
            NewDevice("192.168.1.10"),
            CancellationToken.None
        );

        Assert.True(result.IsT1);

        // Literal, for the reason UserPersistenceContractTests spells out: the constant alone
        // would move with the implementation.
        Assert.Equal("A device is already registered at this address.", result.AsT1.Message);
        Assert.Equal(IDeviceRepository.AddressAlreadyRegistered, result.AsT1.Message);
    }

    // ─── AD-022: only the address index becomes a conflict ───────────────

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

    // ─── AD-007: cancellation is end-to-end, not a threaded parameter ────

    [Fact]
    public async Task Registering_a_device_aborts_when_the_caller_has_already_cancelled()
    {
        await using var context = fixture.CreateDbContext();
        var repository = new DeviceRepository(context);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            repository.AddIfAddressFreeAsync(NewDevice("192.168.1.10"), cancelled.Token)
        );

        await using var verification = fixture.CreateDbContext();
        Assert.Equal(0, await verification.Devices.CountAsync());
    }
}
