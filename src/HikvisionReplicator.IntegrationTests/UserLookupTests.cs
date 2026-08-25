using System.Net;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Answering "is this spectator registered, and what do we hold for them?" during a live event.
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserLookupTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 8, 25, 18, 45, 0, TimeSpan.Zero);

    // ─── USR-35: the whole representation, minus the bytes ───────────────

    [Fact]
    public async Task Registered_spectator_is_returned_with_its_identity_and_timestamps()
    {
        // Driven from a controlled clock: a timestamptz column keeps microseconds, so a value
        // stamped from the system clock is never bit-identical once it has been through the
        // database, and the assertion would be about precision rather than about the timestamps.
        using var factory = WithClock(new FixedTimeProvider(Kickoff));
        using var client = factory.CreateClient();
        var created = await ReadBodyAsync(await UpsertAsync(client, "TICKET-1", ValidUpsert()));

        var response = await ReadAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(created.GetProperty("id").GetInt32(), body.GetProperty("id").GetInt32());
        Assert.Equal("TICKET-1", body.GetProperty("externalRef").GetString());
        Assert.Equal(DefaultName, body.GetProperty("name").GetString());
        Assert.Equal(DefaultAccessCode, body.GetProperty("accessCode").GetString());
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("createdAt").GetDateTime());
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("updatedAt").GetDateTime());
    }

    [Fact]
    public async Task Registered_spectator_reports_the_fingerprint_its_registration_reported()
    {
        var created = await ReadBodyAsync(await UpsertAsync("TICKET-1", ValidUpsert()));

        var body = await ReadBodyAsync(await ReadAsync("TICKET-1"));

        Assert.Equal(
            created.GetProperty("faceContentHash").GetString(),
            body.GetProperty("faceContentHash").GetString()
        );
        Assert.Equal(
            created.GetProperty("faceByteSize").GetInt32(),
            body.GetProperty("faceByteSize").GetInt32()
        );
        Assert.Equal(
            created.GetProperty("faceWidth").GetInt32(),
            body.GetProperty("faceWidth").GetInt32()
        );
        Assert.Equal(
            created.GetProperty("faceHeight").GetInt32(),
            body.GetProperty("faceHeight").GetInt32()
        );
    }

    // ─── USR-37: the bytes never leave the process ───────────────────────

    [Fact]
    public async Task Retrieved_spectator_never_includes_the_face_picture_bytes()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        var picture = await StoredPictureAsync(stored.Id);
        Assert.NotNull(picture);

        var response = await ReadAsync("TICKET-1");

        var body = await ReadBodyAsync(response);
        Assert.False(body.TryGetProperty("facePicture", out _));

        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            Convert.ToBase64String(picture.Content),
            payload,
            StringComparison.Ordinal
        );
    }

    // ─── USR-36: nothing is found where nothing was registered ───────────

    [Fact]
    public async Task Unregistered_reference_reports_not_found()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await ReadAsync("TICKET-9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Reference_no_spectator_could_hold_reports_not_found()
    {
        var response = await ReadAsync(new string('T', ExternalRef.MaxLength + 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── A-15: references are compared byte-exactly ──────────────────────

    [Fact]
    public async Task References_differing_only_by_letter_case_address_different_spectators()
    {
        await UpsertAsync("ticket-1", ValidUpsert(name: "Ada Lovelace", accessCode: "111111"));
        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper", accessCode: "222222"));

        var lower = await ReadBodyAsync(await ReadAsync("ticket-1"));
        var upper = await ReadBodyAsync(await ReadAsync("TICKET-1"));

        Assert.Equal("Ada Lovelace", lower.GetProperty("name").GetString());
        Assert.Equal("Grace Hopper", upper.GetProperty("name").GetString());
        Assert.NotEqual(lower.GetProperty("id").GetInt32(), upper.GetProperty("id").GetInt32());
    }
}
