using System.Net;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Removing a spectator so a refunded ticket stops opening a turnstile.
/// <para>
/// A removal is a tombstone, not a delete: Phase 2 still has to push a Remove to every device,
/// which needs the identity fields but never the biometric. So the row is asserted to survive and
/// the picture is asserted to be gone — and the picture assertion reads
/// <c>face_pictures</c> directly, because "the API no longer shows it" would pass just as
/// happily against a soft delete that left 200 KB of face on disk.
/// </para>
/// </summary>
[Collection(PostgresCollection.Name)]
public class RemoveUserTests(PostgresFixture fixture) : UserApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 8, 25, 18, 45, 0, TimeSpan.Zero);

    // ─── USR-29: the row survives, marked ────────────────────────────────

    [Fact]
    public async Task Registered_spectator_is_removed()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await RemoveAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Removed_spectator_keeps_its_row_and_its_identity_fields()
    {
        var clock = new FixedTimeProvider(Kickoff);
        using var factory = WithClock(clock);
        using var client = factory.CreateClient();
        await UpsertAsync(client, "TICKET-1", ValidUpsert());

        clock.Now = Kickoff.AddMinutes(5);
        await client.DeleteAsync(Route("TICKET-1"));

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal(Kickoff.AddMinutes(5).UtcDateTime, stored.DeletedAt);
        Assert.Equal("TICKET-1", stored.ExternalRef.Value);
        Assert.Equal(DefaultName, stored.Name);
        Assert.Equal(DefaultAccessCode, stored.AccessCode.Value);
        Assert.Equal(1, await CountUsersAsync());
    }

    // ─── USR-30: the biometric is destroyed ──────────────────────────────

    [Fact]
    public async Task Removed_spectators_face_picture_is_destroyed()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        var registered = await StoredUserAsync("TICKET-1");
        Assert.NotNull(registered);
        Assert.NotNull(await StoredPictureAsync(registered.Id));

        await RemoveAsync("TICKET-1");

        Assert.Null(await StoredPictureAsync(registered.Id));
    }

    [Fact]
    public async Task Removal_leaves_no_face_picture_behind_at_all()
    {
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "111111"));
        await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "222222"));

        await RemoveAsync("TICKET-1");

        await using var context = Fixture.CreateDbContext();
        var remaining = context.Set<FacePicture>().ToList();
        var survivor = await StoredUserAsync("TICKET-2");
        Assert.NotNull(survivor);
        Assert.Equal([survivor.Id], remaining.Select(picture => picture.UserId));
    }

    // ─── USR-31: invisible to every read path ────────────────────────────

    [Fact]
    public async Task Removed_spectator_is_no_longer_retrievable()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        await RemoveAsync("TICKET-1");

        var response = await ReadAsync("TICKET-1");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Removed_spectator_is_absent_from_the_catalogue()
    {
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "111111"));
        await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "222222"));

        await RemoveAsync("TICKET-1");

        // Through the catalogue route, not the specification behind it (AD-036). The Verifier
        // found this test still constructing a repository and a spec — the one place the rule
        // its own commit introduced was left violated.
        var body = await ReadBodyAsync(await Client.GetAsync("/api/users"));

        Assert.Equal(
            ["TICKET-2"],
            body.GetProperty("items")
                .EnumerateArray()
                .Select(item => item.GetProperty("externalRef").GetString())
        );
    }

    // ─── USR-32 / A-16: repeating a removal is safe ──────────────────────

    [Fact]
    public async Task Removing_a_spectator_that_is_already_removed_is_accepted()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        await RemoveAsync("TICKET-1");

        var response = await RemoveAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Removing_a_spectator_twice_does_not_move_the_time_it_was_removed()
    {
        var clock = new FixedTimeProvider(Kickoff);
        using var factory = WithClock(clock);
        using var client = factory.CreateClient();
        await UpsertAsync(client, "TICKET-1", ValidUpsert());
        await client.DeleteAsync(Route("TICKET-1"));

        clock.Now = Kickoff.AddHours(1);
        await client.DeleteAsync(Route("TICKET-1"));

        var stored = await StoredUserAsync("TICKET-1");
        Assert.NotNull(stored);
        Assert.Equal(Kickoff.UtcDateTime, stored.DeletedAt);
    }

    // ─── USR-33: nothing was ever registered there ───────────────────────

    [Fact]
    public async Task Removing_an_unregistered_reference_reports_not_found()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await RemoveAsync("TICKET-9");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(1, await CountUsersAsync());
    }

    [Fact]
    public async Task Removing_a_reference_no_spectator_could_hold_reports_not_found()
    {
        var response = await RemoveAsync(new string('T', ExternalRef.MaxLength + 1));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── A-5 / USR-06: the PIN returns to the pool, the key does not ─────

    [Fact]
    public async Task Removed_spectators_access_code_can_be_claimed_by_another_spectator()
    {
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "123456"));
        await RemoveAsync("TICKET-1");

        var response = await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "123456"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var claimant = await StoredUserAsync("TICKET-2");
        Assert.NotNull(claimant);
        Assert.Equal("123456", claimant.AccessCode.Value);
    }
}
