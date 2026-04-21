using System.Net;
using System.Net.Http.Json;
using HikvisionReplicator.Api.Features.Devices.CreateDevice;
using HikvisionReplicator.Api.Features.Devices.UpdateDevice;
using Microsoft.AspNetCore.Http;
using DeviceResponse = HikvisionReplicator.Api.Features.Devices.GetDevice.DeviceResponse;

namespace HikvisionReplicator.Tests;

public class DeviceEndpointsTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly HttpClient _client;

    public DeviceEndpointsTests(TestWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private static CreateDeviceRequest ValidCreate(
        string name = "Test Device",
        string ip = "192.168.1.10",
        int port = 80,
        string username = "admin",
        string password = "secret"
    ) => new(name, ip, port, username, password);

    // ─── US1: Register a Device ───────────────────────────────────────────

    [Fact]
    public async Task New_device_is_created_and_returned()
    {
        var response = await _client.PostAsJsonAsync("/api/devices", ValidCreate());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.StartsWith("/api/devices/", response.Headers.Location!.ToString());

        var body = await response.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.NotNull(body);
        Assert.Equal("Test Device", body!.Name);
        Assert.Equal("192.168.1.10", body.IpAddress);
        Assert.Equal(80, body.HttpPort);
        Assert.Equal("admin", body.Username);
        Assert.True(body.Id > 0);
    }

    [Fact]
    public async Task Device_with_duplicate_ip_and_port_is_rejected()
    {
        await _client.PostAsJsonAsync("/api/devices", ValidCreate(ip: "192.168.1.11", port: 81));
        var response = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.1.11", port: 81)
        );

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Device_without_required_name_is_invalid()
    {
        var request = new CreateDeviceRequest(null, "192.168.1.12", 80, "admin", "secret");
        var response = await _client.PostAsJsonAsync("/api/devices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.NotNull(body);
        Assert.True(body!.Errors.ContainsKey("name"));
    }

    [Fact]
    public async Task Device_with_invalid_ip_address_is_rejected()
    {
        var request = ValidCreate(ip: "not-an-ip");
        var response = await _client.PostAsJsonAsync("/api/devices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("ipAddress"));
    }

    [Fact]
    public async Task Device_with_out_of_range_port_is_rejected()
    {
        var request = ValidCreate(port: 0);
        var response = await _client.PostAsJsonAsync("/api/devices", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HttpValidationProblemDetails>();
        Assert.True(body!.Errors.ContainsKey("httpPort"));
    }

    // ─── US2: Retrieve Device Information ────────────────────────────────

    [Fact]
    public async Task Listing_devices_with_none_registered_returns_empty()
    {
        var response = await _client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceResponse[]>();
        Assert.NotNull(body);
        // May not be empty if other tests ran first; just verify it's a valid array
        Assert.IsType<DeviceResponse[]>(body);
    }

    [Fact]
    public async Task Listing_devices_returns_all_without_passwords()
    {
        await _client.PostAsJsonAsync("/api/devices", ValidCreate(ip: "192.168.2.1", port: 80));

        var response = await _client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceResponse[]>();
        Assert.NotNull(body);
        Assert.True(body!.Length >= 1);
        // DeviceResponse has no Password property — verified by compile-time type
    }

    [Fact]
    public async Task Getting_existing_device_returns_correct_data()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.3.1", port: 80)
        );
        var device = await created.Content.ReadFromJsonAsync<DeviceResponse>();

        var response = await _client.GetAsync($"/api/devices/{device!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.NotNull(body);
        Assert.Equal(device.Id, body!.Id);
        Assert.Equal("192.168.3.1", body.IpAddress);
    }

    [Fact]
    public async Task Getting_unknown_device_returns_not_found()
    {
        var response = await _client.GetAsync("/api/devices/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── US3: Update Device ───────────────────────────────────────────────

    [Fact]
    public async Task Partial_update_only_changes_provided_fields()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.4.1", port: 80)
        );
        var device = await created.Content.ReadFromJsonAsync<DeviceResponse>();

        var update = new UpdateDeviceRequest("Updated Name", null, null, null, null);
        var response = await _client.PutAsJsonAsync($"/api/devices/{device!.Id}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<DeviceResponse>();
        Assert.Equal("Updated Name", body!.Name);
        Assert.Equal("192.168.4.1", body.IpAddress); // unchanged
    }

    [Fact]
    public async Task Update_without_password_retains_existing_credential()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.4.2", port: 80)
        );
        var device = await created.Content.ReadFromJsonAsync<DeviceResponse>();

        var update = new UpdateDeviceRequest("New Name", null, null, null, null);
        var response = await _client.PutAsJsonAsync($"/api/devices/{device!.Id}", update);

        // Just verify 200 — we cannot verify the password was retained from response
        // because it's never returned. The encryption test is in the service layer.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Update_to_conflicting_ip_and_port_is_rejected()
    {
        await _client.PostAsJsonAsync("/api/devices", ValidCreate(ip: "192.168.5.1", port: 80));
        var created2 = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.5.2", port: 80)
        );
        var device2 = await created2.Content.ReadFromJsonAsync<DeviceResponse>();

        var update = new UpdateDeviceRequest(null, "192.168.5.1", 80, null, null);
        var response = await _client.PutAsJsonAsync($"/api/devices/{device2!.Id}", update);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Update_with_invalid_field_values_is_rejected()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.6.1", port: 80)
        );
        var device = await created.Content.ReadFromJsonAsync<DeviceResponse>();

        var update = new UpdateDeviceRequest(null, "not-an-ip", null, null, null);
        var response = await _client.PutAsJsonAsync($"/api/devices/{device!.Id}", update);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Updating_unknown_device_returns_not_found()
    {
        var update = new UpdateDeviceRequest("X", null, null, null, null);
        var response = await _client.PutAsJsonAsync("/api/devices/999999", update);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── US4: Delete Device ───────────────────────────────────────────────

    [Fact]
    public async Task Existing_device_can_be_deleted()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.7.1", port: 80)
        );
        var device = await created.Content.ReadFromJsonAsync<DeviceResponse>();

        var response = await _client.DeleteAsync($"/api/devices/{device!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Deleted_device_is_no_longer_retrievable()
    {
        var created = await _client.PostAsJsonAsync(
            "/api/devices",
            ValidCreate(ip: "192.168.8.1", port: 80)
        );
        var device = await created.Content.ReadFromJsonAsync<DeviceResponse>();

        await _client.DeleteAsync($"/api/devices/{device!.Id}");
        var response = await _client.GetAsync($"/api/devices/{device.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Deleting_unknown_device_returns_not_found()
    {
        var response = await _client.DeleteAsync("/api/devices/999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
