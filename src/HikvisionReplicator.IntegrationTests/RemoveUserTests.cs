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

    // ─── REP-03 / REP-42 / REP-05: a removal queues a removal ────────────

    [Fact]
    public async Task Removing_a_spectator_queues_a_removal_for_every_reader()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        await GivenRegisteredDeviceAsync("10.0.0.1");
        await GivenRegisteredDeviceAsync("10.0.0.2");

        var response = await RemoveAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var queued = await QueuedWorkAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(
            queued,
            work =>
            {
                Assert.Equal(ReplicationOperation.Remove, work.Operation);
                Assert.Equal(ReplicationLane.Live, work.Lane);
                Assert.Equal(ReplicationStatus.Pending, work.Status);
            }
        );
    }

    [Fact]
    public async Task Removing_a_spectator_a_second_time_queues_nothing_further()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        await GivenRegisteredDeviceAsync("10.0.0.1");
        await RemoveAsync("TICKET-1");
        var afterTheFirst = await QueuedWorkAsync();

        // USR-32 and A-16 make the second removal answer success, not 404, which is exactly
        // why this is a live trap: work hung off a successful response would fan out again
        // against a spectator who is already a tombstone.
        var second = await RemoveAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);
        Assert.Single(afterTheFirst);
        Assert.Equal(
            afterTheFirst.Select(work => work.Id),
            (await QueuedWorkAsync()).Select(work => work.Id)
        );
    }

    [Fact]
    public async Task Removing_a_spectator_with_no_readers_queues_nothing()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await RemoveAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await QueuedWorkAsync());
    }

    // ─── REP-11 / REP-41: the removal replaces what is owed and still runs ─

    private static readonly DateTime QueuedAt = new(2026, 10, 3, 10, 0, 0, DateTimeKind.Utc);

    private const string ReaderRefusal = "the reader refused the enrolment";

    /// <summary>
    /// Puts one piece of work in the queue for the pair, in whatever state the caller needs,
    /// so a removal can be asserted against a queue that is not empty.
    /// </summary>
    private async Task<int> GivenQueuedWorkAsync(
        int userId,
        int deviceId,
        ReplicationOperation operation = ReplicationOperation.Add,
        bool alreadyFailed = false,
        bool alreadyInFlight = false
    )
    {
        await using var context = Fixture.CreateDbContext();
        var work = Replication.Create(
            userId,
            deviceId,
            operation,
            ReplicationLane.Live,
            QueuedAt
        );

        if (alreadyFailed)
        {
            work.Begin(QueuedAt);
            work.Fail(ReaderRefusal, QueuedAt);
        }
        else if (alreadyInFlight)
        {
            work.Begin(QueuedAt);
        }

        context.Replications.Add(work);
        await context.SaveChangesAsync();
        return work.Id;
    }

    /// <summary>
    /// Registers a spectator before any reader exists, so the queue starts empty and every
    /// row in it afterwards was put there deliberately.
    /// </summary>
    private async Task<(int UserId, int DeviceId)> GivenSpectatorAndReaderAsync()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        var deviceId = await GivenRegisteredDeviceAsync("10.0.0.1");
        var spectator = await StoredUserAsync("TICKET-1");
        Assert.NotNull(spectator);
        return (spectator.Id, deviceId);
    }

    [Fact]
    public async Task Removing_a_spectator_replaces_the_enrolment_still_owed_and_queues_the_removal()
    {
        var (userId, deviceId) = await GivenSpectatorAndReaderAsync();
        var enrolmentId = await GivenQueuedWorkAsync(userId, deviceId);

        var response = await RemoveAsync("TICKET-1");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var queued = await QueuedWorkAsync();
        Assert.Equal(2, queued.Count);

        var enrolment = Assert.Single(queued, work => work.Id == enrolmentId);
        Assert.Equal(ReplicationStatus.Superseded, enrolment.Status);
        Assert.Equal(ReplicationOperation.Add, enrolment.Operation);

        // The removal still executes: a pending Add is not evidence that no earlier Add ever
        // reached the reader, and the queue does not track what a reader holds.
        var removal = Assert.Single(queued, work => work.Id != enrolmentId);
        Assert.Equal(ReplicationOperation.Remove, removal.Operation);
        Assert.Equal(ReplicationStatus.Pending, removal.Status);
        Assert.Equal(deviceId, removal.DeviceId);
    }

    [Fact]
    public async Task Removing_a_spectator_replaces_an_outstanding_correction_too()
    {
        var (userId, deviceId) = await GivenSpectatorAndReaderAsync();
        var correctionId = await GivenQueuedWorkAsync(
            userId,
            deviceId,
            ReplicationOperation.Update
        );

        await RemoveAsync("TICKET-1");

        var queued = await QueuedWorkAsync();
        var correction = Assert.Single(queued, work => work.Id == correctionId);
        Assert.Equal(ReplicationStatus.Superseded, correction.Status);
        Assert.Equal(ReplicationOperation.Update, correction.Operation);

        var removal = Assert.Single(queued, work => work.Id != correctionId);
        Assert.Equal(ReplicationOperation.Remove, removal.Operation);
        Assert.Equal(ReplicationStatus.Pending, removal.Status);
    }

    [Fact]
    public async Task A_removal_is_queued_even_though_no_reader_ever_accepted_the_spectator()
    {
        var (userId, deviceId) = await GivenSpectatorAndReaderAsync();
        var refusedId = await GivenQueuedWorkAsync(userId, deviceId, alreadyFailed: true);

        await RemoveAsync("TICKET-1");

        var queued = await QueuedWorkAsync();

        // Nothing ever succeeded for this spectator, and the removal is queued anyway: the
        // queue cannot know what a reader holds, so it cannot conclude there is nothing to
        // take off one.
        Assert.DoesNotContain(queued, work => work.Status == ReplicationStatus.Succeeded);

        var refused = Assert.Single(queued, work => work.Id == refusedId);
        Assert.Equal(ReplicationStatus.Failed, refused.Status);
        Assert.Equal(ReaderRefusal, refused.LastError);

        var removal = Assert.Single(queued, work => work.Id != refusedId);
        Assert.Equal(ReplicationOperation.Remove, removal.Operation);
        Assert.Equal(ReplicationStatus.Pending, removal.Status);
    }

    [Fact]
    public async Task Removing_a_spectator_leaves_work_already_in_a_readers_hands_to_finish()
    {
        var (userId, deviceId) = await GivenSpectatorAndReaderAsync();
        var inFlightId = await GivenQueuedWorkAsync(userId, deviceId, alreadyInFlight: true);

        await RemoveAsync("TICKET-1");

        var queued = await QueuedWorkAsync();

        var inFlight = Assert.Single(queued, work => work.Id == inFlightId);
        Assert.Equal(ReplicationStatus.InProgress, inFlight.Status);
        Assert.Equal(ReplicationOperation.Add, inFlight.Operation);

        var removal = Assert.Single(queued, work => work.Id != inFlightId);
        Assert.Equal(ReplicationOperation.Remove, removal.Operation);
        Assert.Equal(ReplicationStatus.Pending, removal.Status);
    }
}
