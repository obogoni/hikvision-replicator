using System.Net;
using System.Text;
using HikvisionReplicator.Api.Domain;
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

    // ─── REP-14 / REP-15 / REP-43: registration records a debt, in one row ─

    [Fact]
    public async Task Registering_a_reader_with_a_full_roster_records_one_debt_and_no_work()
    {
        await GivenActiveSpectatorsAsync(50);

        var response = await RegisterAsync(ValidRegistration());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // One row, not fifty. The whole point of the intent is that registering hardware on
        // match day costs the same whether the roster is empty or a stadium full.
        Assert.Single(await BackfillDebtsAsync());
        Assert.Equal(0, await CountQueuedWorkAsync());
    }

    [Fact]
    public async Task Registering_a_reader_with_no_spectators_still_records_the_debt()
    {
        var response = await RegisterAsync(ValidRegistration());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        // Spectators may arrive before the debt is expanded, so there is nothing to wait for.
        var debt = Assert.Single(await BackfillDebtsAsync());
        Assert.Equal(BackfillStatus.Pending, debt.Status);
        Assert.Null(debt.ExpandedAt);
    }

    [Fact]
    public async Task A_recorded_debt_names_the_reader_the_database_stored()
    {
        var response = await RegisterAsync(ValidRegistration());
        var registered = (await ReadBodyAsync(response)).GetProperty("id").GetInt32();

        var debt = Assert.Single(await BackfillDebtsAsync());

        // The key is issued only after the event was raised, so a handler reading the id off
        // the event would have recorded the debt against reader 0.
        Assert.NotEqual(0, debt.DeviceId);
        Assert.Equal(registered, debt.DeviceId);
    }

    [Fact]
    public async Task Each_reader_is_owed_a_debt_of_its_own()
    {
        await RegisterAsync(ValidRegistration(ipAddress: "10.0.0.1"));
        await RegisterAsync(ValidRegistration(ipAddress: "10.0.0.2"));

        var debts = await BackfillDebtsAsync();

        Assert.Equal(2, debts.Count);
        Assert.Equal(2, debts.Select(debt => debt.DeviceId).Distinct().Count());
    }

    [Fact]
    public async Task A_reader_rejected_for_a_duplicate_address_leaves_no_debt_behind()
    {
        var accepted = (
            await ReadBodyAsync(await RegisterAsync(ValidRegistration(ipAddress: "10.0.0.1")))
        )
            .GetProperty("id")
            .GetInt32();

        var rejected = await RegisterAsync(
            ValidRegistration(ipAddress: "10.0.0.1", name: "Second Reader")
        );

        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

        // One debt, owed to the reader that actually entered the catalogue. A refused
        // attempt that left an intent behind would owe a roster to a reader that does not
        // exist, and the unique device index would then refuse the next real registration
        // at that address.
        Assert.Equal(1, await CountDevicesAsync());
        var debt = Assert.Single(await BackfillDebtsAsync());
        Assert.Equal(accepted, debt.DeviceId);
    }

    // ─── REP-21 / REP-22 / REP-47: a reader that cannot hold the crowd ───

    /// <summary>
    /// REP-21. The literal sentence is pinned here, in one place, rather than compared against
    /// the application's own format string — a tautological assertion moves with the code it
    /// is supposed to hold still (<c>docs/test-patterns.md</c>). Both numbers appear in it
    /// because an operator at a turnstile has to know which reader to swap and for what.
    /// </summary>
    [Fact]
    public async Task Reader_too_small_for_the_active_roster_is_refused()
    {
        await GivenActiveSpectatorsAsync(10);

        var response = await RegisterAsync(ValidRegistration(faceCapacity: 5));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problem = await ReadBodyAsync(response);
        Assert.Equal(
            "This device holds 5 faces, but 10 users are active.",
            problem.GetProperty("detail").GetString()
        );
    }

    [Fact]
    public async Task A_reader_refused_for_capacity_joins_neither_the_catalogue_nor_the_queue()
    {
        await GivenActiveSpectatorsAsync(10);

        var response = await RegisterAsync(ValidRegistration(faceCapacity: 9));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // A debt left behind by the refusal would owe the whole roster to hardware that was
        // never admitted — the REP-43 failure, reached by the other refusal path.
        Assert.Equal(0, await CountDevicesAsync());
        Assert.Empty(await BackfillDebtsAsync());
    }

    /// <summary>
    /// REP-22 at the boundary the guard turns on. A reader holding exactly the crowd holds
    /// the crowd, so equality is admission, not refusal.
    /// </summary>
    [Fact]
    public async Task Reader_sized_exactly_to_the_active_roster_is_registered()
    {
        await GivenActiveSpectatorsAsync(10);

        var response = await RegisterAsync(ValidRegistration(faceCapacity: 10));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountDevicesAsync());
    }

    [Fact]
    public async Task Reader_larger_than_the_active_roster_is_registered()
    {
        await GivenActiveSpectatorsAsync(10);

        var response = await RegisterAsync(ValidRegistration(faceCapacity: 11));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountDevicesAsync());
    }

    /// <summary>REP-47: an empty registry admits the smallest reader there is.</summary>
    [Fact]
    public async Task Reader_of_any_size_is_registered_while_no_spectator_is_active()
    {
        var response = await RegisterAsync(ValidRegistration(faceCapacity: 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(1, await CountDevicesAsync());
    }

    /// <summary>
    /// The ceiling is the <em>active</em> roster (AD-015, AD-034). A tombstoned spectator is
    /// never sent anywhere, so counting them would refuse hardware over faces no reader will
    /// ever be asked to hold — and the registry never deletes a row, so that error grows
    /// without bound over a season.
    /// </summary>
    [Fact]
    public async Task Tombstoned_spectators_do_not_count_against_a_reader_capacity()
    {
        await UpsertSpectatorAsync("TICKET-1", "778811");
        await UpsertSpectatorAsync("TICKET-2", "778822");

        var removal = await Client.DeleteAsync("/api/users/TICKET-2");
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        var response = await RegisterAsync(ValidRegistration(faceCapacity: 1));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
