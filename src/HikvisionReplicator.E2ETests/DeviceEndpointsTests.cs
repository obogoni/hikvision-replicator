using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Playwright;
using Microsoft.Playwright.NUnit;
using NUnit.Framework;

namespace HikvisionReplicator.E2ETests;

internal sealed record DeviceResponse(
    [property: JsonPropertyName("id")]        int      Id,
    [property: JsonPropertyName("name")]      string   Name,
    [property: JsonPropertyName("ipAddress")] string   IpAddress,
    [property: JsonPropertyName("httpPort")]  int      HttpPort,
    [property: JsonPropertyName("username")]  string   Username,
    [property: JsonPropertyName("createdAt")] DateTime CreatedAt,
    [property: JsonPropertyName("updatedAt")] DateTime UpdatedAt
);

[TestFixture]
[Parallelizable(ParallelScope.Self)]
public class DeviceEndpointsTests : PlaywrightTest
{
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
        _api = await Playwright.APIRequest.NewContextAsync(new APIRequestNewContextOptions
        {
            BaseURL           = BaseUrl,
            IgnoreHTTPSErrors = true,
        });
    }

    [TearDown]
    public async Task TearDown()
    {
        await _api.DisposeAsync();
    }

    private static object ValidDevicePayload(string? name = null, string? ip = null, int port = 80)
    {
        var uniqueIp = ip ?? $"10.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(0, 256)}.{Random.Shared.Next(1, 256)}";
        return new
        {
            name      = name ?? "E2E Test Device",
            ipAddress = uniqueIp,
            httpPort  = port,
            username  = "admin",
            password  = "secret",
        };
    }

    private async Task<DeviceResponse> CreateDeviceAsync(object? payload = null)
    {
        payload ??= ValidDevicePayload();
        var response = await _api.PostAsync("/api/devices", new APIRequestContextOptions { DataObject = payload });
        Assert.That(response.Status, Is.EqualTo(201), "Pre-condition: device creation must succeed");
        var device = JsonSerializer.Deserialize<DeviceResponse>(await response.TextAsync(), JsonOptions);
        Assert.That(device, Is.Not.Null, "Pre-condition: device response must be deserializable");
        return device!;
    }

    [Test]
    public async Task Post_ValidDevice_Returns201WithExpectedBody()
    {
        var payload = ValidDevicePayload(name: "Camera Lobby", port: 8080);

        var response = await _api.PostAsync("/api/devices", new APIRequestContextOptions { DataObject = payload });

        Assert.That(response.Status, Is.EqualTo(201));

        var json   = await response.TextAsync();
        var device = JsonSerializer.Deserialize<DeviceResponse>(json, JsonOptions);

        Assert.That(device,          Is.Not.Null);
        Assert.That(device!.Id,      Is.GreaterThan(0));
        Assert.That(device.Name,     Is.EqualTo("Camera Lobby"));
        Assert.That(device.HttpPort, Is.EqualTo(8080));
        Assert.That(device.Username, Is.EqualTo("admin"));
        Assert.That(json,            Does.Not.Contain("password"), "Password must not be exposed in the response body");
    }

    [Test]
    public async Task GetById_AfterCreation_Returns200WithMatchingData()
    {
        var created = await CreateDeviceAsync(ValidDevicePayload(name: "Camera Entrance", port: 554));

        var response = await _api.GetAsync($"/api/devices/{created.Id}");

        Assert.That(response.Status, Is.EqualTo(200));

        var fetched = JsonSerializer.Deserialize<DeviceResponse>(await response.TextAsync(), JsonOptions);

        Assert.That(fetched,           Is.Not.Null);
        Assert.That(fetched!.Id,       Is.EqualTo(created.Id));
        Assert.That(fetched.Name,      Is.EqualTo(created.Name));
        Assert.That(fetched.IpAddress, Is.EqualTo(created.IpAddress));
        Assert.That(fetched.HttpPort,  Is.EqualTo(created.HttpPort));
        Assert.That(fetched.Username,  Is.EqualTo(created.Username));
    }

    [Test]
    public async Task GetById_NonExistentId_Returns404()
    {
        var response = await _api.GetAsync($"/api/devices/{int.MaxValue}");

        Assert.That(response.Status, Is.EqualTo(404));
    }
}
