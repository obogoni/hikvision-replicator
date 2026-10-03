using System.Net;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// <b><c>UpsertUser</c>, part two of three: amendment.</b> Correcting a registered spectator —
/// the update half of the idempotent upsert.
/// <para>
/// PUT is a full representation (A-2) and the face picture is its sole exception (A-4), so every
/// test here that omits a field expects a rejection, and only the one that omits the picture
/// expects the stored image to survive.
/// </para>
/// </summary>
public partial class UpsertUserTests
{
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

    // ─── REP-02 / REP-04 / REP-05: a correction queues an update ─────────
    // The reader is registered after the spectator in these, so the registration queued
    // nothing and whatever is in the queue was put there by the correction.

    [Fact]
    public async Task Correcting_a_spectator_queues_an_update_for_every_reader()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        await GivenRegisteredDeviceAsync("10.0.0.1");
        await GivenRegisteredDeviceAsync("10.0.0.2");

        var response = await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var queued = await QueuedWorkAsync();
        Assert.Equal(2, queued.Count);
        Assert.All(
            queued,
            work =>
            {
                Assert.Equal(ReplicationOperation.Update, work.Operation);
                Assert.Equal(ReplicationLane.Live, work.Lane);
                Assert.Equal(ReplicationStatus.Pending, work.Status);
            }
        );
    }

    [Fact]
    public async Task Re_sending_an_identical_representation_queues_nothing()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());
        await GivenRegisteredDeviceAsync("10.0.0.1");

        var response = await UpsertAsync("TICKET-1", ValidUpsert());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await QueuedWorkAsync());
    }

    [Fact]
    public async Task Correcting_a_spectator_with_no_readers_queues_nothing()
    {
        await UpsertAsync("TICKET-1", ValidUpsert());

        var response = await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await QueuedWorkAsync());
    }

    // ─── REP-08 / REP-10 / REP-12 / REP-13: one outstanding piece of work ─

    /// <summary>
    /// Puts one piece of work in the queue for the pair and leaves it in whatever state the
    /// caller asks for, so supersession can be asserted against a row that already carries a
    /// history (REP-10) or is already in a reader's hands (REP-12).
    /// </summary>
    private async Task<int> GivenQueuedWorkAsync(int userId, int deviceId, bool alreadyFailedOnce)
    {
        await using var context = Fixture.CreateDbContext();
        var work = Replication.Create(
            userId,
            deviceId,
            ReplicationOperation.Add,
            ReplicationLane.Live,
            Kickoff.UtcDateTime
        );

        if (alreadyFailedOnce)
        {
            work.Begin(Kickoff.UtcDateTime);
            work.Fail(QueuedWorkFailure, Kickoff.UtcDateTime);
            work.Retry(Kickoff.UtcDateTime);
        }

        context.Replications.Add(work);
        await context.SaveChangesAsync();
        return work.Id;
    }

    private const string QueuedWorkFailure = "the reader refused the enrolment";

    [Fact]
    public async Task Three_corrections_leave_one_outstanding_piece_of_work_per_reader()
    {
        await GivenRegisteredDeviceAsync("10.0.0.1");
        await UpsertAsync("TICKET-1", ValidUpsert());

        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));
        await UpsertAsync("TICKET-1", ValidUpsert(name: "Katherine Johnson"));

        var queued = await QueuedWorkAsync();

        // Three intents, three rows — the log keeps what was replaced — but only one of them
        // is still owed.
        Assert.Equal(3, queued.Count);
        Assert.Equal(2, queued.Count(work => work.Status == ReplicationStatus.Superseded));

        var outstanding = Assert.Single(
            queued,
            work => work.Status == ReplicationStatus.Pending
        );
        Assert.Equal(ReplicationOperation.Update, outstanding.Operation);
        Assert.Equal(queued.Max(work => work.CreatedAt), outstanding.CreatedAt);
    }

    [Fact]
    public async Task Superseding_replaces_the_work_owed_to_each_reader_separately()
    {
        var first = await GivenRegisteredDeviceAsync("10.0.0.1");
        var second = await GivenRegisteredDeviceAsync("10.0.0.2");
        await UpsertAsync("TICKET-1", ValidUpsert());

        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        var queued = await QueuedWorkAsync();

        Assert.Equal(4, queued.Count);
        foreach (var deviceId in new[] { first, second })
        {
            var forReader = queued.Where(work => work.DeviceId == deviceId).ToList();
            Assert.Equal(2, forReader.Count);
            Assert.Single(forReader, work => work.Status == ReplicationStatus.Pending);
        }
    }

    [Fact]
    public async Task A_superseded_row_keeps_the_intent_and_the_history_it_recorded()
    {
        var deviceId = await GivenRegisteredDeviceAsync("10.0.0.1");
        await UpsertAsync("TICKET-1", ValidUpsert());
        var spectator = await StoredUserAsync("TICKET-1");
        Assert.NotNull(spectator);

        // Clear what the registration queued, then put back one row that has already been
        // attempted and failed once — the only shape that can prove the count and the error
        // survive supersession.
        await ExecuteSqlAsync("DELETE FROM replications");
        var attemptedId = await GivenQueuedWorkAsync(
            spectator.Id,
            deviceId,
            alreadyFailedOnce: true
        );

        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        var replaced = Assert.Single(await QueuedWorkAsync(), work => work.Id == attemptedId);
        Assert.Equal(ReplicationStatus.Superseded, replaced.Status);
        Assert.Equal(ReplicationOperation.Add, replaced.Operation);
        Assert.Equal(ReplicationLane.Live, replaced.Lane);
        Assert.Equal(1, replaced.AttemptCount);
        Assert.Equal(QueuedWorkFailure, replaced.LastError);
    }

    [Fact]
    public async Task Work_already_in_a_readers_hands_is_left_to_finish()
    {
        var deviceId = await GivenRegisteredDeviceAsync("10.0.0.1");
        await UpsertAsync("TICKET-1", ValidUpsert());
        var spectator = await StoredUserAsync("TICKET-1");
        Assert.NotNull(spectator);

        await ExecuteSqlAsync("DELETE FROM replications");
        var inFlightId = await GivenQueuedWorkAsync(
            spectator.Id,
            deviceId,
            alreadyFailedOnce: false
        );
        await ExecuteSqlAsync(
            $"""UPDATE replications SET "Status" = 'InProgress' WHERE "Id" = {inFlightId}"""
        );

        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        var queued = await QueuedWorkAsync();

        // The reader may already have taken the face: saying the write was replaced while it
        // was still being made would be a lie the queue cannot recover from.
        var inFlight = Assert.Single(queued, work => work.Id == inFlightId);
        Assert.Equal(ReplicationStatus.InProgress, inFlight.Status);
        Assert.Equal(ReplicationOperation.Add, inFlight.Operation);

        var alongside = Assert.Single(queued, work => work.Id != inFlightId);
        Assert.Equal(ReplicationStatus.Pending, alongside.Status);
        Assert.Equal(ReplicationOperation.Update, alongside.Operation);
        Assert.Equal(deviceId, alongside.DeviceId);
    }

    [Fact]
    public async Task Work_that_was_already_carried_out_is_left_alone_and_a_new_piece_queued()
    {
        var deviceId = await GivenRegisteredDeviceAsync("10.0.0.1");
        await UpsertAsync("TICKET-1", ValidUpsert());

        await ExecuteSqlAsync("""UPDATE replications SET "Status" = 'Succeeded'""");

        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper"));

        var queued = await QueuedWorkAsync();

        Assert.Equal(2, queued.Count);
        Assert.Single(queued, work => work.Status == ReplicationStatus.Succeeded);
        var outstanding = Assert.Single(queued, work => work.Status == ReplicationStatus.Pending);
        Assert.Equal(ReplicationOperation.Update, outstanding.Operation);
        Assert.Equal(deviceId, outstanding.DeviceId);
    }

    [Fact]
    public async Task Corrections_arriving_at_once_leave_one_outstanding_piece_of_work_per_reader()
    {
        var deviceId = await GivenRegisteredDeviceAsync("10.0.0.1");
        await UpsertAsync("TICKET-1", ValidUpsert());

        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var racers = Enumerable
            .Range(0, 4)
            .Select(async index =>
            {
                await start.Task;
                return await UpsertAsync("TICKET-1", ValidUpsert(name: $"Correction {index}"));
            })
            .ToList();

        start.SetResult();
        var responses = await Task.WhenAll(racers);

        // Which racer wins is scheduling, so nothing is asserted about that. What REP-13
        // promises is that the queue is never left with two outstanding rows for a pair and
        // that no caller is ever told the service broke: the index is the arbiter, and its
        // refusal is a conflict.
        Assert.DoesNotContain(
            responses,
            response => response.StatusCode == HttpStatusCode.InternalServerError
        );

        var queued = await QueuedWorkAsync();
        var outstanding = Assert.Single(
            queued,
            work => work.Status == ReplicationStatus.Pending && work.DeviceId == deviceId
        );

        // One intent in full, not a mixture: a row is one insert, so its lane and status have
        // to belong to the operation beside them.
        Assert.Equal(ReplicationOperation.Update, outstanding.Operation);
        Assert.Equal(ReplicationLane.Live, outstanding.Lane);

        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task Correcting_a_spectator_leaves_another_spectators_outstanding_work_alone()
    {
        await GivenRegisteredDeviceAsync("10.0.0.1");
        await UpsertAsync("TICKET-1", ValidUpsert(accessCode: "111111"));
        await UpsertAsync("TICKET-2", ValidUpsert(accessCode: "222222"));
        var bystander = await StoredUserAsync("TICKET-2");
        Assert.NotNull(bystander);

        await UpsertAsync("TICKET-1", ValidUpsert(name: "Grace Hopper", accessCode: "111111"));

        var untouched = Assert.Single(
            await QueuedWorkAsync(),
            work => work.UserId == bystander.Id
        );
        Assert.Equal(ReplicationStatus.Pending, untouched.Status);
        Assert.Equal(ReplicationOperation.Add, untouched.Operation);
    }
}
