using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Events;

namespace HikvisionReplicator.Tests.Domain;

/// <summary>
/// What the reader aggregate says happened. A backfill is owed on registration only, so
/// an event raised from an update would queue the fleet again for nothing.
/// </summary>
public class DeviceEventTests
{
    private static readonly DateTime RegisteredOn = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 3, 19, 0, 0, DateTimeKind.Utc);

    private static Device Created() =>
        Device.Create("Turnstile A", "10.0.0.5", 80, "admin", "cipher", 50_000, RegisteredOn).AsT0;

    /// <summary>A registered reader, as a save leaves it: catalogued, and nothing owed.</summary>
    private static Device Registered()
    {
        var device = Created();
        device.ClearDomainEvents();
        return device;
    }

    // ─── REP-14: registration is announced, exactly once ───

    [Fact]
    public void Registering_a_reader_announces_a_registration()
    {
        var device = Created();

        var raised = Assert.Single(device.DomainEvents);
        var registered = Assert.IsType<DeviceRegistered>(raised);
        Assert.Equal(RegisteredOn, registered.OccurredAt);
    }

    // ─── REP-14: a reader that was already catalogued is owed no second backfill ───

    [Theory]
    [InlineData("Turnstile B", null, null)]
    [InlineData(null, "10.0.0.6", null)]
    [InlineData(null, null, 60_000)]
    public void Changing_a_registered_reader_announces_nothing(
        string? name,
        string? ipAddress,
        int? faceCapacity
    )
    {
        var device = Registered();

        var result = device.Update(name, ipAddress, null, null, null, faceCapacity, Later);

        Assert.True(result.IsT0);
        Assert.Equal(Later, device.UpdatedAt);
        Assert.Empty(device.DomainEvents);
    }

    [Fact]
    public void A_reader_update_that_changes_nothing_announces_nothing()
    {
        var device = Registered();

        device.Update("Turnstile A", "10.0.0.5", 80, "admin", "cipher", 50_000, Later);

        Assert.Empty(device.DomainEvents);
    }

    // ─── The announcement carries the reader, never a key the database has not issued ───

    /// <summary>
    /// The same defect as on the spectator side: the registration is announced from inside the
    /// factory, where the key is still <c>0</c>. An intent staged from that number would point
    /// at a reader that does not exist, so the event carries the aggregate instead.
    /// </summary>
    [Fact]
    public void A_registration_announces_the_reader_itself_before_the_database_has_keyed_it()
    {
        var device = Created();

        var registered = Assert.IsType<DeviceRegistered>(Assert.Single(device.DomainEvents));

        Assert.Same(device, registered.Device);
        Assert.Equal(0, registered.Device.Id);
    }
}
