using System.Net;
using System.Security.Cryptography;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Registering a spectator the registry has never seen — the create half of the idempotent
/// upsert (A-2).
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserRegistrationTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 8, 25, 18, 45, 0, TimeSpan.Zero);

    // ─── USR-01: a valid registration ────────────────────────────────────

    [Fact]
    public async Task New_spectator_is_created_and_returned()
    {
        var response = await UpsertAsync("TICKET-1", ValidUpsert());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal("TICKET-1", body.GetProperty("externalRef").GetString());
        Assert.Equal(DefaultName, body.GetProperty("name").GetString());
        Assert.Equal(DefaultAccessCode, body.GetProperty("accessCode").GetString());
        Assert.True(body.GetProperty("id").GetInt32() > 0);
        Assert.Equal(1, await CountUsersAsync());
    }

    [Fact]
    public async Task New_spectator_is_addressed_by_the_reference_the_caller_chose()
    {
        var response = await UpsertAsync("TICKET-1", ValidUpsert());

        Assert.Equal(Route("TICKET-1"), response.Headers.Location?.ToString());
    }

    // ─── USR-09 / USR-22: the fingerprint, never the bytes ───────────────

    [Fact]
    public async Task Spectator_carries_the_fingerprint_of_the_picture_that_was_stored()
    {
        var response = await UpsertAsync("TICKET-1", ValidUpsert());

        var body = await ReadBodyAsync(response);
        var user = await StoredUserAsync("TICKET-1");
        Assert.NotNull(user);
        var picture = await StoredPictureAsync(user.Id);
        Assert.NotNull(picture);

        Assert.Equal(
            Convert.ToHexStringLower(SHA256.HashData(picture.Content)),
            body.GetProperty("faceContentHash").GetString()
        );
        Assert.Equal(picture.Content.Length, body.GetProperty("faceByteSize").GetInt32());
        Assert.True(body.GetProperty("faceWidth").GetInt32() > 0);
        Assert.True(body.GetProperty("faceHeight").GetInt32() > 0);
    }

    [Fact]
    public async Task Spectator_response_never_includes_the_face_picture_bytes()
    {
        var response = await UpsertAsync("TICKET-1", ValidUpsert());

        var body = await ReadBodyAsync(response);
        Assert.False(body.TryGetProperty("facePicture", out _));

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        var picture = await StoredPictureAsync(stored.Id);
        Assert.NotNull(picture);

        var payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(
            Convert.ToBase64String(picture.Content),
            payload,
            StringComparison.Ordinal
        );
    }

    // ─── USR-11: the clock is injected ───────────────────────────────────

    [Fact]
    public async Task Spectator_is_stamped_with_the_time_the_application_was_given()
    {
        using var factory = WithClock(new FixedTimeProvider(Kickoff));
        using var client = factory.CreateClient();

        var response = await UpsertAsync(client, "TICKET-1", ValidUpsert());

        var body = await ReadBodyAsync(response);
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("createdAt").GetDateTime());
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("updatedAt").GetDateTime());
    }

    // ─── USR-05 / A-3: a spectator cannot exist without a face ───────────

    [Fact]
    public async Task Spectator_without_a_face_picture_is_rejected()
    {
        var response = await UpsertAsync(
            "TICKET-1",
            new { name = DefaultName, accessCode = DefaultAccessCode }
        );

        await AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_whose_face_picture_is_not_an_image_is_rejected()
    {
        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(fixture: FaceFixtures.NotAnImage)
        );

        await AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_whose_face_picture_is_below_the_resolution_floor_is_rejected()
    {
        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(fixture: FaceFixtures.SubFloorThumbnail)
        );

        await AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    // ─── USR-02 / USR-03 / USR-04: the identity fields ───────────────────

    [Fact]
    public async Task Spectator_with_a_blank_external_reference_is_rejected()
    {
        var response = await UpsertAsync(" ", ValidUpsert());

        await AssertRejectedFieldAsync(response, ExternalRef.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_with_an_overlong_external_reference_is_rejected()
    {
        var response = await UpsertAsync(new string('T', ExternalRef.MaxLength + 1), ValidUpsert());

        await AssertRejectedFieldAsync(response, ExternalRef.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_without_a_name_is_rejected()
    {
        var response = await UpsertAsync(
            "TICKET-1",
            new { accessCode = DefaultAccessCode, facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait) }
        );

        await AssertRejectedFieldAsync(response, User.Errors.NameField);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_whose_name_is_only_whitespace_is_rejected()
    {
        var response = await UpsertAsync("TICKET-1", ValidUpsert(name: "   "));

        await AssertRejectedFieldAsync(response, User.Errors.NameField);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_with_an_overlong_name_is_rejected()
    {
        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(name: new string('A', User.MaxNameLength + 1))
        );

        await AssertRejectedFieldAsync(response, User.Errors.NameField);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_without_an_access_code_is_rejected()
    {
        var response = await UpsertAsync(
            "TICKET-1",
            new { name = DefaultName, facePicture = FaceFixtures.Bytes(FaceFixtures.Portrait) }
        );

        await AssertRejectedFieldAsync(response, AccessCode.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("123456789012345678901")]
    public async Task Spectator_with_an_unusable_access_code_is_rejected(string accessCode)
    {
        var response = await UpsertAsync("TICKET-1", ValidUpsert(accessCode: accessCode));

        await AssertRejectedFieldAsync(response, AccessCode.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectator_whose_access_code_uses_non_ascii_digits_is_rejected()
    {
        // Arabic-Indic digits read as "1234" but no device keypad can produce them.
        var response = await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "١٢٣٤"));

        await AssertRejectedFieldAsync(response, AccessCode.Errors.Field);
        Assert.Equal(0, await CountUsersAsync());
    }

    // ─── USR-06 / USR-07 / USR-08: uniqueness under load ─────────────────

    [Fact]
    public async Task Spectator_claiming_an_active_access_code_is_rejected_as_a_conflict()
    {
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "123456"));

        var response = await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "123456"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal(1, await CountUsersAsync());
    }

    [Fact]
    public async Task Spectators_registered_at_once_under_one_reference_yield_one_user()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racers = Enumerable
            .Range(0, 4)
            .Select(async index =>
            {
                await start.Task;
                return await UpsertAsync("TICKET-1", ValidUpsert(accessCode: $"10000{index}"));
            })
            .ToList();

        start.SetResult();
        var responses = await Task.WhenAll(racers);

        // The loser must be told its key is taken, not handed a 500 — the whole point of
        // translating the constraint violation inside the repository (AD-022).
        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.Created),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
        );
        Assert.Equal(1, await CountUsersAsync());

        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task Spectators_claiming_one_access_code_at_once_yield_one_user()
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racers = Enumerable
            .Range(0, 4)
            .Select(async index =>
            {
                await start.Task;
                return await UpsertAsync($"TICKET-{index}", ValidUpsert(accessCode: "123456"));
            })
            .ToList();

        start.SetResult();
        var responses = await Task.WhenAll(racers);

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses.Where(response => response.StatusCode != HttpStatusCode.Created),
            response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode)
        );
        Assert.Equal(1, await CountUsersAsync());

        foreach (var response in responses)
            response.Dispose();
    }

    // ─── USR-10: the user and its picture are one transaction ────────────

    [Fact]
    public async Task Spectator_is_not_stored_when_its_face_picture_cannot_be_written()
    {
        // Refuses every insert into face_pictures at the database, which is the only way to make
        // the second half of the write fail after the first half has already been staged.
        await ExecuteSqlAsync(
            """ALTER TABLE face_pictures ADD CONSTRAINT refuse_writes CHECK (false)"""
        );

        try
        {
            var response = await UpsertAsync("TICKET-1", ValidUpsert());

            Assert.False(response.IsSuccessStatusCode);
            Assert.Equal(0, await CountUsersAsync());
        }
        finally
        {
            await ExecuteSqlAsync(
                """ALTER TABLE face_pictures DROP CONSTRAINT refuse_writes"""
            );
        }
    }
}
