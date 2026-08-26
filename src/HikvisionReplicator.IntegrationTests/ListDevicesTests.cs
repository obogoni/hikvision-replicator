using System.Net;
using System.Text;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Listing the catalogue — DEV-08 and DEV-09.
/// </summary>
[Collection(PostgresCollection.Name)]
public class ListDevicesTests(PostgresFixture fixture) : DeviceApiTests(fixture)
{
    // ─── DEV-08 / DEV-09: listing the catalogue ──────────────────────────

    [Fact]
    public async Task Listing_devices_with_none_registered_returns_empty()
    {
        var response = await Client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(JsonValueKind.Array, body.ValueKind);
        Assert.Empty(body.EnumerateArray());
    }

    [Fact]
    public async Task Every_registered_device_appears_in_the_catalogue()
    {
        await RegisterAsync(ValidRegistration(ipAddress: "192.168.1.10", name: "Front Gate"));
        await RegisterAsync(ValidRegistration(ipAddress: "192.168.1.11", name: "Back Gate"));

        var response = await Client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var listed = (await ReadBodyAsync(response)).EnumerateArray().ToList();
        Assert.Equal(2, listed.Count);

        var frontGate = listed.Single(device =>
            device.GetProperty("name").GetString() == "Front Gate"
        );
        Assert.True(frontGate.GetProperty("id").GetInt32() > 0);
        Assert.Equal("192.168.1.10", frontGate.GetProperty("ipAddress").GetString());
        Assert.Equal(80, frontGate.GetProperty("httpPort").GetInt32());
        Assert.Equal("admin", frontGate.GetProperty("username").GetString());
        Assert.Equal(10_000, frontGate.GetProperty("faceCapacity").GetInt32());
        Assert.NotEqual(default, frontGate.GetProperty("createdAt").GetDateTime());
        Assert.NotEqual(default, frontGate.GetProperty("updatedAt").GetDateTime());

        Assert.Contains(
            listed,
            device => device.GetProperty("ipAddress").GetString() == "192.168.1.11"
        );
    }

    [Fact]
    public async Task Listed_devices_never_include_the_password()
    {
        await RegisterAsync(ValidRegistration());

        var response = await Client.GetAsync("/api/devices");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SentinelPassword, json);

        var listed = (await ReadBodyAsync(response)).EnumerateArray().Single();
        Assert.DoesNotContain(
            listed.EnumerateObject(),
            property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
        );
    }
}
