using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Tests;

[Collection(PostgresCollection.Name)]
public class DeviceEndpointsTests(PostgresFixture fixture) : IAsyncLifetime
{
    private const string SentinelPassword = "s3cr3t-Passw0rd";

    private readonly HttpClient _client = fixture.Factory.CreateClient();

    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private static object ValidRegistration(
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

    private Task<HttpResponseMessage> RegisterAsync(object request) =>
        _client.PostAsJsonAsync("/api/devices", request);

    private static async Task<JsonElement> ReadBodyAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    /// <summary>Asserts a 400 problem body whose validation errors name the given field.</summary>
    private static async Task AssertRejectedFieldAsync(
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

    private async Task<int> CountDevicesAsync()
    {
        await using var db = fixture.CreateDbContext();
        return await db.Devices.CountAsync();
    }

    // ─── DEV-01: a valid registration ────────────────────────────────────

    [Fact]
    public async Task New_device_is_created_and_returned()
    {
        var response = await RegisterAsync(ValidRegistration());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadBodyAsync(response);
        var id = body.GetProperty("id").GetInt32();

        Assert.Equal($"/api/devices/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Front Gate Reader", body.GetProperty("name").GetString());
        Assert.Equal("192.168.1.10", body.GetProperty("ipAddress").GetString());
        Assert.Equal(80, body.GetProperty("httpPort").GetInt32());
        Assert.Equal("admin", body.GetProperty("username").GetString());
        Assert.Equal(10_000, body.GetProperty("faceCapacity").GetInt32());
        Assert.Equal(
            body.GetProperty("createdAt").GetDateTime(),
            body.GetProperty("updatedAt").GetDateTime()
        );
    }

    // ─── DEV-02: required fields ─────────────────────────────────────────

    [Fact]
    public async Task Device_without_a_name_is_invalid()
    {
        var response = await RegisterAsync(
            new
            {
                ipAddress = "192.168.1.10",
                httpPort = 80,
                username = "admin",
                password = SentinelPassword,
                faceCapacity = 10_000,
            }
        );

        await AssertRejectedFieldAsync(response, "name");
    }

    [Fact]
    public async Task Device_with_a_blank_name_is_invalid()
    {
        var response = await RegisterAsync(ValidRegistration(name: "   "));

        await AssertRejectedFieldAsync(response, "name");
    }

    [Fact]
    public async Task Device_without_an_ip_address_is_invalid()
    {
        var response = await RegisterAsync(
            new
            {
                name = "Front Gate Reader",
                httpPort = 80,
                username = "admin",
                password = SentinelPassword,
                faceCapacity = 10_000,
            }
        );

        await AssertRejectedFieldAsync(response, "ipAddress");
    }

    [Fact]
    public async Task Device_without_an_http_port_is_invalid()
    {
        var response = await RegisterAsync(
            new
            {
                name = "Front Gate Reader",
                ipAddress = "192.168.1.10",
                username = "admin",
                password = SentinelPassword,
                faceCapacity = 10_000,
            }
        );

        await AssertRejectedFieldAsync(response, "httpPort");
    }

    [Fact]
    public async Task Device_without_a_username_is_invalid()
    {
        var response = await RegisterAsync(
            new
            {
                name = "Front Gate Reader",
                ipAddress = "192.168.1.10",
                httpPort = 80,
                password = SentinelPassword,
                faceCapacity = 10_000,
            }
        );

        await AssertRejectedFieldAsync(response, "username");
    }

    [Fact]
    public async Task Device_without_a_password_is_invalid()
    {
        var response = await RegisterAsync(
            new
            {
                name = "Front Gate Reader",
                ipAddress = "192.168.1.10",
                httpPort = 80,
                username = "admin",
                faceCapacity = 10_000,
            }
        );

        await AssertRejectedFieldAsync(response, "password");
    }

    [Fact]
    public async Task Device_with_a_blank_password_is_invalid()
    {
        var response = await RegisterAsync(ValidRegistration(password: "   "));

        await AssertRejectedFieldAsync(response, "password");
    }

    [Fact]
    public async Task Device_without_a_face_capacity_is_invalid()
    {
        var response = await RegisterAsync(
            new
            {
                name = "Front Gate Reader",
                ipAddress = "192.168.1.10",
                httpPort = 80,
                username = "admin",
                password = SentinelPassword,
            }
        );

        await AssertRejectedFieldAsync(response, "faceCapacity");
    }

    // ─── DEV-03: name and username length ────────────────────────────────

    [Fact]
    public async Task Device_name_of_exactly_one_hundred_characters_is_accepted()
    {
        var response = await RegisterAsync(ValidRegistration(name: new string('n', 100)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Device_name_longer_than_one_hundred_characters_is_invalid()
    {
        var response = await RegisterAsync(ValidRegistration(name: new string('n', 101)));

        await AssertRejectedFieldAsync(response, "name");
    }

    [Fact]
    public async Task Device_username_of_exactly_one_hundred_characters_is_accepted()
    {
        var response = await RegisterAsync(ValidRegistration(username: new string('u', 100)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Device_username_longer_than_one_hundred_characters_is_invalid()
    {
        var response = await RegisterAsync(ValidRegistration(username: new string('u', 101)));

        await AssertRejectedFieldAsync(response, "username");
    }

    // ─── DEV-04: address, port and capacity ranges ───────────────────────

    [Fact]
    public async Task Device_with_an_unparseable_ip_address_is_invalid()
    {
        var response = await RegisterAsync(ValidRegistration(ipAddress: "not-an-address"));

        await AssertRejectedFieldAsync(response, "ipAddress");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public async Task Device_with_an_http_port_outside_the_permitted_range_is_invalid(int httpPort)
    {
        var response = await RegisterAsync(ValidRegistration(httpPort: httpPort));

        await AssertRejectedFieldAsync(response, "httpPort");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public async Task Device_on_a_boundary_http_port_is_accepted(int httpPort)
    {
        var response = await RegisterAsync(ValidRegistration(httpPort: httpPort));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public async Task Device_with_a_face_capacity_outside_the_permitted_range_is_invalid(
        int faceCapacity
    )
    {
        var response = await RegisterAsync(ValidRegistration(faceCapacity: faceCapacity));

        await AssertRejectedFieldAsync(response, "faceCapacity");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public async Task Device_with_a_boundary_face_capacity_is_accepted(int faceCapacity)
    {
        var response = await RegisterAsync(ValidRegistration(faceCapacity: faceCapacity));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ─── DEV-05 / DEV-06: one device per address ─────────────────────────

    [Fact]
    public async Task Device_reusing_a_registered_address_is_rejected()
    {
        await RegisterAsync(ValidRegistration());

        var response = await RegisterAsync(ValidRegistration(name: "Back Gate Reader"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountDevicesAsync());
    }

    [Fact]
    public async Task Address_written_in_a_non_canonical_form_collides_with_its_canonical_form()
    {
        var first = await RegisterAsync(ValidRegistration(ipAddress: "192.168.1.1"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var response = await RegisterAsync(ValidRegistration(ipAddress: "192.168.001.001"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountDevicesAsync());
    }

    [Fact]
    public async Task Simultaneous_registrations_of_one_address_yield_a_single_device()
    {
        const int attempts = 8;

        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, attempts)
                .Select(attempt => RegisterAsync(ValidRegistration(name: $"Reader {attempt}")))
        );

        var statuses = responses.Select(response => response.StatusCode).ToList();

        Assert.Equal(1, statuses.Count(status => status == HttpStatusCode.Created));
        Assert.Equal(attempts - 1, statuses.Count(status => status == HttpStatusCode.Conflict));
        Assert.DoesNotContain(HttpStatusCode.InternalServerError, statuses);
        Assert.Equal(1, await CountDevicesAsync());
    }

    // ─── DEV-07: the password never escapes ──────────────────────────────

    [Fact]
    public async Task Device_response_never_includes_the_password()
    {
        var response = await RegisterAsync(ValidRegistration());
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SentinelPassword, json);

        var body = await ReadBodyAsync(response);
        Assert.DoesNotContain(
            body.EnumerateObject(),
            property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public async Task Stored_password_is_neither_the_plaintext_nor_empty()
    {
        await RegisterAsync(ValidRegistration());

        await using var db = fixture.CreateDbContext();
        var stored = await db.Devices.SingleAsync();

        Assert.False(string.IsNullOrWhiteSpace(stored.EncryptedPassword));
        Assert.NotEqual(SentinelPassword, stored.EncryptedPassword);
        Assert.DoesNotContain(SentinelPassword, stored.EncryptedPassword);
    }

    // ─── DEV-10: retrieving one device ───────────────────────────────────

    [Fact]
    public async Task Registered_device_is_retrieved_by_its_id()
    {
        var registration = await RegisterAsync(ValidRegistration());
        var registered = await ReadBodyAsync(registration);
        var id = registered.GetProperty("id").GetInt32();

        var response = await _client.GetAsync($"/api/devices/{id}");

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
        var response = await _client.GetAsync("/api/devices/999999");

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

        var response = await _client.GetAsync(location);

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

        var response = await _client.GetAsync($"/api/devices/{id}");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SentinelPassword, json);

        var body = await ReadBodyAsync(response);
        Assert.DoesNotContain(
            body.EnumerateObject(),
            property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
        );
    }

    // ─── DEV-08 / DEV-09: listing the catalogue ──────────────────────────

    [Fact]
    public async Task Listing_devices_with_none_registered_returns_empty()
    {
        var response = await _client.GetAsync("/api/devices");

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

        var response = await _client.GetAsync("/api/devices");

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

        var response = await _client.GetAsync("/api/devices");
        var json = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain(SentinelPassword, json);

        var listed = (await ReadBodyAsync(response)).EnumerateArray().Single();
        Assert.DoesNotContain(
            listed.EnumerateObject(),
            property => property.Name.Contains("password", StringComparison.OrdinalIgnoreCase)
        );
    }

    // ─── DEV-18…DEV-23: amending a device ────────────────────────────────

    /// <summary>Registers a device and returns it as the database reports it.</summary>
    private async Task<(int Id, JsonElement Device)> GivenRegisteredDeviceAsync(
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
        return (id, await ReadBodyAsync(await _client.GetAsync($"/api/devices/{id}")));
    }

    private Task<HttpResponseMessage> UpdateAsync(int id, object request) =>
        _client.PutAsJsonAsync($"/api/devices/{id}", request);

    private async Task<string> ReadStoredPasswordAsync(int id)
    {
        await using var db = fixture.CreateDbContext();
        return (await db.Devices.SingleAsync(device => device.Id == id)).EncryptedPassword;
    }

    [Fact]
    public async Task Device_name_is_amended_and_every_other_field_is_left_alone()
    {
        var (id, original) = await GivenRegisteredDeviceAsync();

        var response = await UpdateAsync(id, new { name = "Side Gate Reader" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var reread = await ReadBodyAsync(await _client.GetAsync($"/api/devices/{id}"));
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

        var reread = await ReadBodyAsync(await _client.GetAsync($"/api/devices/{id}"));
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

        var reread = await ReadBodyAsync(await _client.GetAsync($"/api/devices/{movingId}"));
        Assert.Equal(
            original.GetProperty("ipAddress").GetString(),
            reread.GetProperty("ipAddress").GetString()
        );
        Assert.Equal(
            original.GetProperty("updatedAt").GetDateTime(),
            reread.GetProperty("updatedAt").GetDateTime()
        );

        var occupier = await ReadBodyAsync(await _client.GetAsync($"/api/devices/{occupiedId}"));
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

    // ─── DEV-11 / DEV-24 / DEV-25: removing a device ─────────────────────

    [Fact]
    public async Task Removed_device_is_no_longer_retrievable()
    {
        var (id, _) = await GivenRegisteredDeviceAsync();

        var removal = await _client.DeleteAsync($"/api/devices/{id}");

        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        var lookup = await _client.GetAsync($"/api/devices/{id}");
        Assert.Equal(HttpStatusCode.NotFound, lookup.StatusCode);
    }

    [Fact]
    public async Task Removed_device_no_longer_appears_in_the_catalogue()
    {
        var (removedId, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.10");
        var (keptId, _) = await GivenRegisteredDeviceAsync(ipAddress: "192.168.1.11");

        await _client.DeleteAsync($"/api/devices/{removedId}");

        var listed = (await ReadBodyAsync(await _client.GetAsync("/api/devices")))
            .EnumerateArray()
            .ToList();

        Assert.DoesNotContain(listed, device => device.GetProperty("id").GetInt32() == removedId);
        Assert.Contains(listed, device => device.GetProperty("id").GetInt32() == keptId);
    }

    [Fact]
    public async Task Removing_a_device_that_was_never_registered_returns_not_found()
    {
        var response = await _client.DeleteAsync("/api/devices/999999");

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
        await _client.DeleteAsync($"/api/devices/{id}");

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
        await _client.DeleteAsync($"/api/devices/{id}");

        var removed = await _client.GetAsync($"/api/devices/{id}");
        var neverRegistered = await _client.GetAsync("/api/devices/999999");

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

    private static async Task<Dictionary<string, string>> ProblemFieldsAsync(
        HttpResponseMessage response
    ) =>
        (await ReadBodyAsync(response))
            .EnumerateObject()
            .Where(property => property.Name != "traceId")
            .ToDictionary(property => property.Name, property => property.Value.ToString());

    // ─── Edge case: an unparseable request body ──────────────────────────

    [Fact]
    public async Task Malformed_request_body_is_rejected_as_a_bad_request()
    {
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await _client.PostAsync("/api/devices", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );
    }
}
