using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Registering a device — DEV-01 through DEV-07. Validation lives with the situation whose
/// request carries the field (AD-037), so the field rules are here rather than in a separate
/// validation class.
/// </summary>
[Collection(PostgresCollection.Name)]
public class RegisterDeviceTests(PostgresFixture fixture) : DeviceApiTests(fixture)
{
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
        var problem = await ReadBodyAsync(response);
        Assert.Equal(
            IDeviceRepository.AddressAlreadyRegistered,
            problem.GetProperty("detail").GetString()
        );
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

        await using var db = Fixture.CreateDbContext();
        var stored = await db.Devices.SingleAsync();

        Assert.False(string.IsNullOrWhiteSpace(stored.EncryptedPassword));
        Assert.NotEqual(SentinelPassword, stored.EncryptedPassword);
        Assert.DoesNotContain(SentinelPassword, stored.EncryptedPassword);
    }

    // ─── Edge case: an unparseable request body ──────────────────────────

    [Fact]
    public async Task Malformed_request_body_is_rejected_as_a_bad_request()
    {
        var content = new StringContent("{ not json", Encoding.UTF8, "application/json");

        var response = await Client.PostAsync("/api/devices", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType
        );
    }
}
