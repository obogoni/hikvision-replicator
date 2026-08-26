using System.Net;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Correcting a registered spectator — the update half of the idempotent upsert.
/// <para>
/// PUT is a full representation (A-2) and the face picture is its sole exception (A-4), so every
/// test here that omits a field expects a rejection, and only the one that omits the picture
/// expects the stored image to survive.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class UserAmendmentTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 8, 25, 18, 45, 0, TimeSpan.Zero);

    private async Task<byte[]> StoredPictureContentAsync(string externalRef)
    {
        var user = await StoredUserAsync(externalRef);
        Assert.NotNull(user);
        var picture = await StoredPictureAsync(user.Id);
        Assert.NotNull(picture);
        return picture.Content;
    }

    private static object NameOnlyUpsert(string name, string accessCode = DefaultAccessCode) =>
        new { name, accessCode };

    // ─── USR-23: an existing reference is rewritten, not duplicated ──────

    [Fact]
    public async Task Registered_spectator_is_corrected_and_returned()
    {
        var created = await ReadBodyAsync(await UpsertAsync("TICKET-1", ValidUpsert()));

        var response = await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal("Grace Hopper", body.GetProperty("name").GetString());
        Assert.Equal(created.GetProperty("id").GetInt32(), body.GetProperty("id").GetInt32());
        Assert.Equal(1, await CountUsersAsync());
    }

    // ─── USR-24: omitting the picture keeps the stored image ─────────────

    [Fact]
    public async Task Correction_that_omits_the_face_picture_keeps_the_stored_image()
    {
        var created = await ReadBodyAsync(await UpsertAsync("TICKET-1", ValidUpsert()));
        var storedBefore = await StoredPictureContentAsync("TICKET-1");

        var body = await ReadBodyAsync(
            await UpsertAsync("TICKET-1", NameOnlyUpsert("Grace Hopper"))
        );

        Assert.Equal("Grace Hopper", body.GetProperty("name").GetString());
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
        Assert.Equal(storedBefore, await StoredPictureContentAsync("TICKET-1"));
    }

    // ─── USR-25: supplying a picture replaces the stored image ───────────

    [Fact]
    public async Task Correction_that_supplies_a_face_picture_replaces_the_stored_image()
    {
        var created = await ReadBodyAsync(await UpsertAsync("TICKET-1", ValidUpsert()));
        var storedBefore = await StoredPictureContentAsync("TICKET-1");

        var body = await ReadBodyAsync(
            await UpsertAsync("TICKET-1", ValidUpsert(fixture: FaceFixtures.Progressive))
        );

        var storedAfter = await StoredPictureContentAsync("TICKET-1");
        Assert.NotEqual(storedBefore, storedAfter);
        Assert.NotEqual(
            created.GetProperty("faceContentHash").GetString(),
            body.GetProperty("faceContentHash").GetString()
        );
        Assert.Equal(storedAfter.Length, body.GetProperty("faceByteSize").GetInt32());
    }

    [Fact]
    public async Task Replacing_a_face_picture_leaves_one_stored_image_for_the_spectator()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(fixture: FaceFixtures.Progressive)
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = Fixture.CreateDbContext();
        Assert.Equal(1, await context.Set<FacePicture>().CountAsync());
    }

    // ─── USR-26: no change means no touch ────────────────────────────────

    [Fact]
    public async Task Re_sending_an_identical_representation_leaves_the_correction_time_unmoved()
    {
        var clock = new FixedTimeProvider(Kickoff);
        using var factory = WithClock(clock);
        using var client = factory.CreateClient();

        var created = await ReadBodyAsync(await UpsertAsync(client, "TICKET-1", ValidUpsert()));

        clock.Now = Kickoff.AddMinutes(5);
        var body = await ReadBodyAsync(await UpsertAsync(client, "TICKET-1", ValidUpsert()));

        Assert.Equal(
            created.GetProperty("updatedAt").GetDateTime(),
            body.GetProperty("updatedAt").GetDateTime()
        );
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("updatedAt").GetDateTime());
    }

    [Fact]
    public async Task Correcting_a_spectator_moves_the_correction_time_to_the_clocks_reading()
    {
        var clock = new FixedTimeProvider(Kickoff);
        using var factory = WithClock(clock);
        using var client = factory.CreateClient();

        await UpsertAsync(client, "TICKET-1", ValidUpsert());

        clock.Now = Kickoff.AddMinutes(5);
        var body = await ReadBodyAsync(
            await UpsertAsync(client, "TICKET-1", ValidUpsert(name: "Grace Hopper"))
        );

        Assert.Equal(Kickoff.AddMinutes(5).UtcDateTime, body.GetProperty("updatedAt").GetDateTime());
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("createdAt").GetDateTime());
    }

    // ─── USR-27: a rejected correction changes nothing ───────────────────

    [Fact]
    public async Task Rejected_correction_leaves_the_stored_spectator_untouched()
    {
        var clock = new FixedTimeProvider(Kickoff);
        using var factory = WithClock(clock);
        using var client = factory.CreateClient();

        var created = await ReadBodyAsync(await UpsertAsync(client, "TICKET-1", ValidUpsert()));
        var storedBefore = await StoredPictureContentAsync("TICKET-1");

        clock.Now = Kickoff.AddMinutes(5);
        var response = await UpsertAsync(client, "TICKET-1", ValidUpsert(name: "   "));

        await AssertRejectedFieldAsync(response, User.Errors.NameField);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal(DefaultName, stored.Name);
        Assert.Equal(created.GetProperty("faceContentHash").GetString(), stored.Face.ContentHash);
        Assert.Equal(Kickoff.UtcDateTime, stored.UpdatedAt);
        Assert.Equal(storedBefore, await StoredPictureContentAsync("TICKET-1"));
    }

    [Fact]
    public async Task Correction_with_an_unusable_face_picture_leaves_the_stored_image_untouched()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        var storedBefore = await StoredPictureContentAsync("TICKET-1");

        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(name: "Grace Hopper", fixture: FaceFixtures.SubFloorThumbnail)
        );

        await AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal(DefaultName, stored.Name);
        Assert.Equal(storedBefore, await StoredPictureContentAsync("TICKET-1"));
    }

    [Fact]
    public async Task Correction_that_omits_the_name_is_rejected()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        // Unlike a device patch, a spectator PUT is a full representation: an omitted name is a
        // missing field, never an instruction to keep the stored one (A-2).
        var response = await UpsertAsync("TICKET-1", new { accessCode = DefaultAccessCode });

        await AssertRejectedFieldAsync(response, User.Errors.NameField);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal(DefaultName, stored.Name);
    }

    [Fact]
    public async Task Correction_that_omits_the_access_code_is_rejected()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await UpsertAsync("TICKET-1", new { name = "Grace Hopper" });

        await AssertRejectedFieldAsync(response, AccessCode.Errors.Field);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal(DefaultAccessCode, stored.AccessCode.Value);
    }

    // ─── USR-28: access codes stay exclusive among active spectators ─────

    [Fact]
    public async Task Correction_taking_another_active_spectators_access_code_is_a_conflict()
    {
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "111111"));
        await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "222222"));

        var response = await UpsertAsync(
            "TICKET-2",
            ValidUpsert(name: "Grace Hopper", accessCode: "111111")
        );

        await AssertConflictAsync(response, IUserRepository.AccessCodeAlreadyInUse);

        var stored = await StoredUserAsync("TICKET-2");
        Assert.NotNull(stored);
        Assert.Equal("222222", stored.AccessCode.Value);
        Assert.Equal(DefaultName, stored.Name);
    }

    [Fact]
    public async Task Spectator_re_sending_its_own_access_code_is_not_in_conflict_with_itself()
    {
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "111111"));

        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(name: "Grace Hopper", accessCode: "111111")
        );

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal("111111", stored.AccessCode.Value);
        Assert.Equal("Grace Hopper", stored.Name);
    }
}
