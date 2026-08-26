using System.Net;
using System.Text;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Removing a device — DEV-11, DEV-24 and DEV-25.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RemoveDeviceTests(PostgresFixture fixture) : DeviceApiTests(fixture)
{
    // ─── DEV-11 / DEV-24 / DEV-25: removing a device ─────────────────────

    [Fact]
    public async Task Removed_device_is_no_longer_retrievable()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();

        var removal = await Client.DeleteAsync($"/api/devices/{id}");

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        var lookup = await Client.GetAsync($"/api/devices/{id}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task Removed_device_no_longer_appears_in_the_catalogue()
    {
        var (removedId, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.10");
        var (keptId, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.11");

        await Client.DeleteAsync($"/api/devices/{removedId}");

        var listed = (await ReadBodyAsync(await Client.GetAsync("/api/devices")))
            .EnumerateArray()
            .ToList();

        Assert.DoesNotContain(listed, device => device.GetProperty("id").GetInt32() == removedId);
        Assert.Contains(listed, device => device.GetProperty("id").GetInt32() == keptId);
    }

    [Fact]
    public async Task Removing_a_device_that_was_never_registered_returns_not_found()
    {
        var response = await Client.DeleteAsync("/api/devices/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );
    }

    [Fact]
    public async Task Address_of_a_removed_device_is_free_for_a_new_registration()
    {
        var (id, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.10");
        await Client.DeleteAsync($"/api/devices/{id}");

        var response = await RegisterAsync(
            ValidRegistration(ipAddress: "192.168.1.10", name: "Replacement Reader")
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.NotEqual(id, body.GetProperty("id").GetInt32());
        Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString());
    }

    [Fact]
    public async Task Removed_device_and_one_that_never_existed_are_indistinguishable()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();
        await Client.DeleteAsync($"/api/devices/{id}");

        var removed = await Client.GetAsync($"/api/devices/{id}");
        var neverRegistered = await Client.GetAsync("/api/devices/999999");

        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Equal(neverRegistered.StatusCode, removed.StatusCode);

        // Everything the caller could read the device's history from must match. The
        // per-request traceId is excluded: it differs on every request whatever the
        // resource, so it says nothing about whether the device ever existed.
        Assert.Equal(
            await ProblemFieldsAsync(neverRegistered),
            await ProblemFieldsAsync(removed)
        );
    }
}
