using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// What every device-endpoint test class needs: the running application, a clean registry, and
/// the few readings that have to come from the database rather than from the API — because a
/// promise about what is stored cannot be proved by asking the thing that stores it.
/// <para>
/// The mirror of <see cref="UserApiTests"/>, extracted when AD-037 split the one
/// <c>DeviceEndpointsTests</c> class into one class per use case.
/// </para>
/// </summary>
public abstract class DeviceApiTests(PostgresFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Distinctive enough that a substring search proves a leak rather than coincidence — see
    /// <see cref="CredentialLeakageTests"/>, which sweeps for this exact value (DEV-07).
    /// </summary>
    protected const string SentinelPassword = "s3cr3t-Passw0rd";

    protected PostgresFixture Fixture { get; } = fixture;

    protected HttpClient Client { get; } = fixture.Factory.CreateClient();

    public Task InitializeAsync() => Fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    protected static object ValidRegistration(
        string ipAddress = "192.168.1.10",
        int httpPort = 80,
        string name = "Front Gate Reader",
        string username = "admin",
        string password = SentinelPassword,
        int faceCapacity = 10_000
    ) =>
        new
        {
            name,
            ipAddress,
            httpPort,
            username,
            password,
            faceCapacity,
        };

    protected Task<HttpResponseMessage> RegisterAsync(object request) =>
        Client.PostAsJsonAsync("/api/devices", request);

    protected Task<HttpResponseMessage> UpdateAsync(int id, object request) =>
        Client.PutAsJsonAsync($"/api/devices/{id}", request);

    protected static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts a 400 problem body whose validation errors name the given field.</summary>
    protected static async Task AssertRejectedFieldAsync(
        HttpResponseMessage response,
        string expectedField
    )
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await ReadBodyAsync(response);
        var errors = body.GetProperty("errors");
        Assert.True(
            errors.TryGetProperty(expectedField, out var messages),
            $"Expected the problem body to name '{expectedField}'. Body: {body}"
        );
        Assert.NotEmpty(messages.EnumerateArray());
    }

    protected static async Task<Dictionary<string, string>> ProblemFieldsAsync(
        HttpResponseMessage response
    ) =>
        (await ReadBodyAsync(response))
            .EnumerateObject()
            .Where(property => property.Name != "traceId")
            .ToDictionary(property => property.Name, property => property.Value.ToString());

    protected async Task<int> CountDevicesAsync()
    {
        await using var db = Fixture.CreateDbContext();
        return await db.Devices.CountAsync();
    }

    /// <summary>Registers a device and returns it as the database reports it.</summary>
    protected async Task<(int Id, JsonElement Device)> GivenRegisteredDeviceAsync(
        string ipAddress = "192.168.1.10",
        string name = "Front Gate Reader",
        string password = SentinelPassword
    )
    {
        var registration = await RegisterAsync(
            ValidRegistration(ipAddress: ipAddress, name: name, password: password)
        );
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var id = (await ReadBodyAsync(registration)).GetProperty("id").GetInt32();

        // Read back through the API so every timestamp comparison uses the value the
        // database stores, not the finer-grained in-memory one.
        return (id, await ReadBodyAsync(await Client.GetAsync($"/api/devices/{id}")));
    }

    /// <summary>The ciphertext as stored, which the API deliberately never returns (DEV-07).</summary>
    protected async Task<string> ReadStoredPasswordAsync(int id)
    {
        await using var db = Fixture.CreateDbContext();
        return (await db.Devices.SingleAsync(device => device.Id == id)).EncryptedPassword;
    }
}
