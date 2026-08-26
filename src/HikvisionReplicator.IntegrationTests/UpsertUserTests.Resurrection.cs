using System.Net;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// <b><c>UpsertUser</c>, part three of three: resurrection.</b> A PUT naming a removed spectator
/// brings them back (A-7, USR-34).
/// <para>
/// The whole path hangs on the upsert looking the reference up <em>including tombstones</em>. An
/// active-only lookup would report the reference as unregistered, take the create branch, and
/// collide with the external-reference index that deliberately outlives the removal — so these
/// tests assert the spectator comes back in the row it already had, which no other outcome
/// produces.
/// </para>
/// </summary>
public partial class UpsertUserTests
{
    private async Task<int> GivenRemovedSpectatorAsync(
        string externalRef = "TICKET-1",
        string accessCode = DefaultAccessCode
    )
    {
        var created = await ReadBodyAsync(
            await UpsertAsync(externalRef, ValidUpsert(accessCode: accessCode))
        );
        var response = await RemoveAsync(externalRef);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        return created.GetProperty("id").GetInt32();
    }

    // ─── USR-34: the tombstone is cleared and the record rewritten ───────

    [Fact]
    public async Task Removed_spectator_is_registered_again_in_the_row_it_already_had()
    {
        var originalId = await GivenRemovedSpectatorAsync();

        var response = await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(originalId, body.GetProperty("id").GetInt32());
        Assert.Equal("Grace Hopper", body.GetProperty("name").GetString());

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Null(stored.DeletedAt);
        Assert.Equal("Grace Hopper", stored.Name);
        Assert.Equal(1, await CountUsersAsync());
    }

    // USR-31 makes a removed spectator report as not found on every read path, so from the
    // integrator's side the reference genuinely does not exist. Answering 200 here would claim
    // the record had been there all along. The surviving row is our bookkeeping for Phase 2's
    // Remove work (A-5), never something the caller can observe.
    [Fact]
    public async Task Spectator_that_reads_as_missing_is_registered_rather_than_corrected()
    {
        await GivenRemovedSpectatorAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await ReadAsync("TICKET-1")).StatusCode);

        var response = await UpsertAsync("TICKET-1", ValidUpsert());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("/api/users/TICKET-1", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Resurrected_spectator_is_retrievable_again()
    {
        await GivenRemovedSpectatorAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await ReadAsync("TICKET-1")).StatusCode);

        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await ReadAsync("TICKET-1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await ReadBodyAsync(response);
        Assert.Equal(DefaultName, body.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Resurrected_spectator_is_stamped_with_the_time_it_came_back()
    {
        var clock = new FixedTimeProvider(Kickoff);
        using var factory = WithClock(clock);
        using var client = factory.CreateClient();
        await UpsertAsync(client, "TICKET-1", ValidUpsert());
        await client.DeleteAsync(Route("TICKET-1"));

        clock.Now = Kickoff.AddHours(1);
        var body = await ReadBodyAsync(await UpsertAsync(client, "TICKET-1", ValidUpsert()));

        Assert.Equal(Kickoff.AddHours(1).UtcDateTime, body.GetProperty("updatedAt").GetDateTime());
        Assert.Equal(Kickoff.UtcDateTime, body.GetProperty("createdAt").GetDateTime());
    }

    // ─── A-3 / A-7: a face is mandatory again ────────────────────────────

    [Fact]
    public async Task Resurrection_without_a_face_picture_is_rejected()
    {
        await GivenRemovedSpectatorAsync();

        // A-4's "omitting it keeps the stored image" has nothing to keep: the removal destroyed
        // the picture, so this is a creation as far as validation is concerned.
        var response = await UpsertAsync(
            "TICKET-1",
            new { name = DefaultName, accessCode = DefaultAccessCode }
        );

        await AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.NotNull(stored.DeletedAt);
        Assert.Null(await StoredPictureAsync(stored.Id));
    }

    [Fact]
    public async Task Resurrected_spectator_has_a_stored_face_picture_again()
    {
        var originalId = await GivenRemovedSpectatorAsync();
        Assert.Null(await StoredPictureAsync(originalId));

        var body = await ReadBodyAsync(await UpsertAsync("TICKET-1", ValidUpsert()));

        var picture = await StoredPictureAsync(originalId);
        Assert.NotNull(picture);
        Assert.NotEmpty(picture.Content);
        Assert.Equal(picture.Content.Length, body.GetProperty("faceByteSize").GetInt32());
    }

    [Fact]
    public async Task Resurrection_with_an_unusable_face_picture_leaves_the_tombstone_in_place()
    {
        await GivenRemovedSpectatorAsync();

        var response = await UpsertAsync(
            "TICKET-1",
            ValidUpsert(fixture: FaceFixtures.SubFloorThumbnail)
        );

        await AssertRejectedFieldAsync(response, FaceFingerprint.Errors.Field);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.NotNull(stored.DeletedAt);
    }

    // ─── USR-06: the access code is re-checked against active users ──────

    [Fact]
    public async Task Resurrection_taking_an_active_spectators_access_code_is_a_conflict()
    {
        await GivenRemovedSpectatorAsync("TICKET-1", "111111");
        await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "222222"));

        var response = await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "222222"));

        await AssertConflictAsync(response, IUserRepository.AccessCodeAlreadyInUse);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.NotNull(stored.DeletedAt);
    }

    [Fact]
    public async Task Resurrection_reclaiming_the_access_code_it_held_before_is_accepted()
    {
        await GivenRemovedSpectatorAsync("TICKET-1", "111111");

        var response = await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "111111"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Null(stored.DeletedAt);
        Assert.Equal("111111", stored.AccessCode.Value);
    }

    [Fact]
    public async Task Resurrection_without_a_name_is_rejected()
    {
        await GivenRemovedSpectatorAsync();

        var response = await UpsertAsync("TICKET-1", ValidUpsert(name: "   "));

        await AssertRejectedFieldAsync(response, User.Errors.NameField);

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.NotNull(stored.DeletedAt);
    }
}
