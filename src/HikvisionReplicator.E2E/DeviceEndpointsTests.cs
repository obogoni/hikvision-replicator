using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace HikvisionReplicator.E2E;

internal sealed record DeviceResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("ipAddress")] string IpAddress,
    [property: JsonPropertyName("httpPort")] int HttpPort,
    [property: JsonPropertyName("username")] string Username,
    [property: JsonPropertyName("faceCapacity")] int FaceCapacity,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt
);

/// <summary>
/// An out-of-process confirmation that the five device routes behave over real HTTP
/// against a live API. Depth lives in the integration suite; this is the thin end-to-end
/// pass. The target is <c>E2E_BASE_URL</c>, defaulting to the local development address.
/// </summary>
[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class DeviceEndpointsTests : PlaywrightTest
{
    private const string SentinelPassword = "e2e-s3cr3t-Passw0rd";
    private const int UnknownDeviceId = int.MaxValue;

    private static readonly string BaseUrl =
        Environment.GetEnvironmentVariable("E2E_BASE_URL") ?? "http://localhost:5000";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private IAPIRequestContext _api = null!;

    [SetUp]
    public async Task SetUp()
    {
        _api = await Playwright.APIRequest.NewContextAsync(
            new APIRequestNewContextOptions { BaseURL = BaseUrl, IgnoreHTTPSErrors = true }
        );
    }

    [TearDown]
    public async Task TearDown() => await _api.DisposeAsync();

    /// <summary>The catalogue outlives a run, so every device claims a fresh address.</summary>
    private static string UniqueAddress() =>
        $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 256)}";

    private static object Registration(
        string? name = null,
        string? ipAddress = null,
        int httpPort = 80,
        int faceCapacity = 10_000
    ) =>
        new
        {
            name = name ?? "E2E Gate Reader",
            ipAddress = ipAddress ?? UniqueAddress(),
            httpPort,
            username = "admin",
            password = SentinelPassword,
            faceCapacity,
        };

    private Task<IAPIResponse> PostDeviceAsync(object payload) =>
        _api.PostAsync("/api/devices", new APIRequestContextOptions { DataObject = payload });

    private static async Task<DeviceResponse> ReadDeviceAsync(IAPIResponse response)
    {
        var device = JsonSerializer.Deserialize<DeviceResponse>(
            await response.TextAsync(),
            JsonOptions
        );
        Assert.That(device, Is.Not.Null);
        return device!;
    }

    private async Task<DeviceResponse> GivenARegisteredDevice(object? payload = null)
    {
        var response = await PostDeviceAsync(payload ?? Registration());
        Assert.That(response.Status, Is.EqualTo(201), "Pre-condition: registration must succeed");
        return await ReadDeviceAsync(response);
    }

    // ─── Registering ─────────────────────────────────────────────────────

    [Test]
    public async Task New_device_is_created_and_returned()
    {
        var address = UniqueAddress();

        var response = await PostDeviceAsync(
            Registration(name: "Camera Lobby", ipAddress: address, httpPort: 8080)
        );

        Assert.That(response.Status, Is.EqualTo(201));

        var body = await response.TextAsync();
        var device = JsonSerializer.Deserialize<DeviceResponse>(body, JsonOptions);

        Assert.That(device, Is.Not.Null);
        Assert.That(device!.Id, Is.GreaterThan(0));
        Assert.That(device.Name, Is.EqualTo("Camera Lobby"));
        Assert.That(device.IpAddress, Is.EqualTo(address));
        Assert.That(device.HttpPort, Is.EqualTo(8080));
        Assert.That(device.Username, Is.EqualTo("admin"));
        Assert.That(device.FaceCapacity, Is.EqualTo(10_000));
        Assert.That(response.Headers["location"], Is.EqualTo($"/api/devices/{device.Id}"));
        Assert.That(body, Does.Not.Contain(SentinelPassword));
    }

    [Test]
    public async Task Device_with_a_duplicate_address_is_rejected()
    {
        var existing = await GivenARegisteredDevice();

        var response = await PostDeviceAsync(
            Registration(ipAddress: existing.IpAddress, httpPort: existing.HttpPort)
        );

        Assert.That(response.Status, Is.EqualTo(409));
    }

    // ─── Retrieving ──────────────────────────────────────────────────────

    [Test]
    public async Task Registered_device_is_retrievable()
    {
        var created = await GivenARegisteredDevice(Registration(name: "Camera Entrance"));

        var response = await _api.GetAsync($"/api/devices/{created.Id}");

        Assert.That(response.Status, Is.EqualTo(200));

        var fetched = await ReadDeviceAsync(response);

        Assert.That(fetched.Id, Is.EqualTo(created.Id));
        Assert.That(fetched.Name, Is.EqualTo("Camera Entrance"));
        Assert.That(fetched.IpAddress, Is.EqualTo(created.IpAddress));
        Assert.That(fetched.HttpPort, Is.EqualTo(created.HttpPort));
        Assert.That(fetched.FaceCapacity, Is.EqualTo(created.FaceCapacity));
    }

    [Test]
    public async Task Getting_unknown_device_returns_not_found()
    {
        var response = await _api.GetAsync($"/api/devices/{UnknownDeviceId}");

        Assert.That(response.Status, Is.EqualTo(404));
    }

    // ─── Listing ─────────────────────────────────────────────────────────

    [Test]
    public async Task Registered_device_appears_in_the_catalogue()
    {
        var created = await GivenARegisteredDevice();

        var response = await _api.GetAsync("/api/devices");

        Assert.That(response.Status, Is.EqualTo(200));

        var catalogue = JsonSerializer.Deserialize<List<DeviceResponse>>(
            await response.TextAsync(),
            JsonOptions
        );

        Assert.That(catalogue, Is.Not.Null);
        Assert.That(catalogue!.Select(device => device.Id), Does.Contain(created.Id));
    }

    // ─── Amending ────────────────────────────────────────────────────────

    [Test]
    public async Task Device_name_is_amended_without_disturbing_its_address()
    {
        var created = await GivenARegisteredDevice();

        // The timestamps are compared against the stored representation, not against the
        // registration response: PostgreSQL keeps microseconds where the response carries
        // the in-memory tick precision, and that difference is not a change.
        var stored = await ReadDeviceAsync(await _api.GetAsync($"/api/devices/{created.Id}"));

        var response = await _api.PutAsync(
            $"/api/devices/{created.Id}",
            new APIRequestContextOptions { DataObject = new { name = "Camera Renamed" } }
        );

        Assert.That(response.Status, Is.EqualTo(200));

        var amended = await ReadDeviceAsync(response);

        Assert.That(amended.Name, Is.EqualTo("Camera Renamed"));
        Assert.That(amended.IpAddress, Is.EqualTo(created.IpAddress));
        Assert.That(amended.HttpPort, Is.EqualTo(created.HttpPort));
        Assert.That(amended.FaceCapacity, Is.EqualTo(created.FaceCapacity));
        Assert.That(amended.CreatedAt, Is.EqualTo(stored.CreatedAt));
        Assert.That(amended.UpdatedAt, Is.GreaterThan(stored.UpdatedAt));
    }

    [Test]
    public async Task Amending_unknown_device_returns_not_found()
    {
        var response = await _api.PutAsync(
            $"/api/devices/{UnknownDeviceId}",
            new APIRequestContextOptions { DataObject = new { name = "Nowhere" } }
        );

        Assert.That(response.Status, Is.EqualTo(404));
    }

    // ─── Removing ────────────────────────────────────────────────────────

    [Test]
    public async Task Removed_device_is_no_longer_retrievable()
    {
        var created = await GivenARegisteredDevice();

        var removal = await _api.DeleteAsync($"/api/devices/{created.Id}");

        Assert.That(removal.Status, Is.EqualTo(204));

        var lookup = await _api.GetAsync($"/api/devices/{created.Id}");

        Assert.That(lookup.Status, Is.EqualTo(404));
    }

    [Test]
    public async Task Removing_unknown_device_returns_not_found()
    {
        var response = await _api.DeleteAsync($"/api/devices/{UnknownDeviceId}");

        Assert.That(response.Status, Is.EqualTo(404));
    }
}
