using System.Net;
using System.Text;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Amending a device — DEV-18 through DEV-23.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UpdateDeviceTests(PostgresFixture fixture) : DeviceApiTests(fixture)
{
    // ─── DEV-18…DEV-23: amending a device ────────────────────────────────

    [Fact]
    public async Task Device_name_is_amended_and_every_other_field_is_left_alone()
    {
        var (id, original) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { name = "Side Gate Reader" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reread = await ReadBodyAsync(await Client.GetAsync($"/api/devices/{id}"));
        Assert.Equal("Side Gate Reader", reread.GetProperty("name").GetString());
        Assert.Equal(
            original.GetProperty("ipAddress").GetString(),
            reread.GetProperty("ipAddress").GetString()
        );
        Assert.Equal(
            original.GetProperty("httpPort").GetInt32(),
            reread.GetProperty("httpPort").GetInt32()
        );
        Assert.Equal(
            original.GetProperty("username").GetString(),
            reread.GetProperty("username").GetString()
        );
        Assert.Equal(
            original.GetProperty("faceCapacity").GetInt32(),
            reread.GetProperty("faceCapacity").GetInt32()
        );
    }

    [Fact]
    public async Task Rejected_update_persists_no_partial_change()
    {
        var (id, original) = await GivenRegisteredDeviceAsync();

        // A valid new name alongside an out-of-range port: neither may survive.
        var response = await UpdateAsync(id, new { name = "Side Gate Reader", httpPort = 0 });

        await AssertRejectedFieldAsync(response, "httpPort");

        var reread = await ReadBodyAsync(await Client.GetAsync($"/api/devices/{id}"));
        Assert.Equal(original.GetProperty("name").GetString(), reread.GetProperty("name").GetString());
        Assert.Equal(
            original.GetProperty("httpPort").GetInt32(),
            reread.GetProperty("httpPort").GetInt32()
        );
        Assert.Equal(
            original.GetProperty("updatedAt").GetDateTime(),
            reread.GetProperty("updatedAt").GetDateTime()
        );
    }

    [Fact]
    public async Task Update_naming_an_unparseable_ip_address_is_invalid()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { ipAddress = "not-an-address" });

        await AssertRejectedFieldAsync(response, "ipAddress");
    }

    [Fact]
    public async Task Update_with_an_over_long_name_is_invalid()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { name = new string('n', 101) });

        await AssertRejectedFieldAsync(response, "name");
    }

    [Fact]
    public async Task Update_with_a_face_capacity_outside_the_permitted_range_is_invalid()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { faceCapacity = 1_000_001 });

        await AssertRejectedFieldAsync(response, "faceCapacity");
    }

    [Fact]
    public async Task Moving_a_device_onto_another_devices_address_is_rejected()
    {
        var (occupiedId, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.10");
        var (movingId, original) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.11");

        var response = await UpdateAsync(movingId, new { ipAddress = "192.168.1.10" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadBodyAsync(response);
        Assert.Equal(
            IDeviceRepository.AddressAlreadyRegistered,
            problem.GetProperty("detail").GetString()
        );

        var reread = await ReadBodyAsync(await Client.GetAsync($"/api/devices/{movingId}"));
        Assert.Equal(
            original.GetProperty("ipAddress").GetString(),
            reread.GetProperty("ipAddress").GetString()
        );
        Assert.Equal(
            original.GetProperty("updatedAt").GetDateTime(),
            reread.GetProperty("updatedAt").GetDateTime()
        );

        var occupier = await ReadBodyAsync(await Client.GetAsync($"/api/devices/{occupiedId}"));
        Assert.Equal("192.168.1.10", occupier.GetProperty("ipAddress").GetString());
    }

    [Fact]
    public async Task Resubmitting_a_devices_own_address_is_accepted()
    {
        var (id, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.10");

        var response = await UpdateAsync(id, new { ipAddress = "192.168.1.10", httpPort = 80 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString());
        Assert.Equal(80, body.GetProperty("httpPort").GetInt32());
    }

    [Fact]
    public async Task Update_that_omits_the_password_leaves_the_stored_one_untouched()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();
        var storedBefore = await ReadStoredPasswordAsync(id);

        var response = await UpdateAsync(id, new { name = "Side Gate Reader" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(storedBefore, await ReadStoredPasswordAsync(id));
    }

    [Fact]
    public async Task Update_that_supplies_a_password_replaces_the_stored_one()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();
        var storedBefore = await ReadStoredPasswordAsync(id);

        var response = await UpdateAsync(id, new { password = "a-different-Passw0rd" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var storedAfter = await ReadStoredPasswordAsync(id);
        Assert.NotEqual(storedBefore, storedAfter);
        Assert.DoesNotContain("a-different-Passw0rd", storedAfter);
    }

    [Fact]
    public async Task Update_with_a_blank_password_is_invalid()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();
        var storedBefore = await ReadStoredPasswordAsync(id);

        var response = await UpdateAsync(id, new { password = "   " });

        await AssertRejectedFieldAsync(response, "password");
        Assert.Equal(storedBefore, await ReadStoredPasswordAsync(id));
    }

    [Fact]
    public async Task Updating_a_device_that_was_never_registered_returns_not_found()
    {
        var response = await UpdateAsync(999999, new { name = "Side Gate Reader" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );
    }

    [Fact]
    public async Task Real_change_advances_the_update_timestamp_but_not_the_creation_timestamp()
    {
        var (id, original) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { name = "Side Gate Reader" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.True(
            body.GetProperty("updatedAt").GetDateTime()
                > original.GetProperty("updatedAt").GetDateTime(),
            "A real change must advance updatedAt."
        );
        Assert.Equal(
            original.GetProperty("createdAt").GetDateTime(),
            body.GetProperty("createdAt").GetDateTime()
        );
    }

    [Fact]
    public async Task Update_that_changes_nothing_leaves_the_update_timestamp_unadvanced()
    {
        var (id, original) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(
            original.GetProperty("updatedAt").GetDateTime(),
            body.GetProperty("updatedAt").GetDateTime()
        );
        Assert.Equal(
            original.GetProperty("name").GetString(),
            body.GetProperty("name").GetString()
        );
    }

    [Fact]
    public async Task Amended_device_response_never_includes_the_password()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { name = "Side Gate Reader" });
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SentinelPassword, json);

        var body = await ReadBodyAsync(response);
        Assert.DoesNotContain(
            body.EnumerateObject(),
            property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
        );
    }
}
