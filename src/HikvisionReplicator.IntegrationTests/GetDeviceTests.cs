using System.Net;
using System.Text;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Retrieving one device — DEV-10.
/// </summary>
[Collection(PostgresCollection.Name)]
public class GetDeviceTests(PostgresFixture fixture) : DeviceApiTests(fixture)
{
    // ─── DEV-10: retrieving one device ───────────────────────────────────

    [Fact]
    public async Task Registered_device_is_retrieved_by_its_id()
    {
        var registration = await RegisterAsync(ValidRegistration());
        var registered = await ReadBodyAsync(registration);
        var id = registered.GetProperty("id").GetInt32();

        var response = await Client.GetAsync($"/api/devices/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(id, body.GetProperty("id").GetInt32());
        Assert.Equal("Front Gate Reader", body.GetProperty("name").GetString());
        Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString());
        Assert.Equal(80, body.GetProperty("httpPort").GetInt32());
        Assert.Equal("admin", body.GetProperty("username").GetString());
        Assert.Equal(10_000, body.GetProperty("faceCapacity").GetInt32());
    }

    [Fact]
    public async Task Getting_a_device_that_was_never_registered_returns_not_found()
    {
        var response = await Client.GetAsync("/api/devices/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );

        var body = await ReadBodyAsync(response);
        Assert.Equal(404, body.GetProperty("status").GetInt32());
    }

    [Fact]
    public async Task Device_is_retrievable_at_the_location_it_reports()
    {
        var registration = await RegisterAsync(ValidRegistration());
        var location = registration.Headers.Location!.ToString();
        var registered = await ReadBodyAsync(registration);

        var response = await Client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(registered.GetProperty("id").GetInt32(), body.GetProperty("id").GetInt32());
        Assert.Equal(
            registered.GetProperty("ipAddress").GetString(),
            body.GetProperty("ipAddress").GetString()
        );
    }

    [Fact]
    public async Task Retrieved_device_never_includes_the_password()
    {
        var registration = await RegisterAsync(ValidRegistration());
        var id = (await ReadBodyAsync(registration)).GetProperty("id").GetInt32();

        var response = await Client.GetAsync($"/api/devices/{id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SentinelPassword, json);

        var body = await ReadBodyAsync(response);
        Assert.DoesNotContain(
            body.EnumerateObject(),
            property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
        );
    }
}
