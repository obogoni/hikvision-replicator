using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Work a worker had in hand when it went away (WRK-22, WRK-23): a crashed worker's claim is
/// reclaimed once its lease runs out, and a worker that is stopped puts its in-flight work back
/// itself. Neither costs the item an attempt, and neither replays intent a newer one replaced.
/// <para>
/// A crashed worker leaves nothing to drive through a route, so its leftovers — a row in hand
/// since long ago — are arranged by hand. A stop is real: a host of its own is shut down while
/// its call is with the reader.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class WorkerRecoveryTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private static readonly DateTime Earlier = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

    /// <summary>Well past the harness's 30-second lease.</summary>
    private static DateTime LongAgo => DateTime.UtcNow.AddMinutes(-1);

    // ─── WRK-22: a crashed worker's claim ───

    /// <summary>
    /// Two readers, each with one row in hand: one claimed a minute ago by a worker that is gone,
    /// one claimed just now by a worker that is still mid-call. The abandoned row carries an
    /// attempt from an earlier refusal, so "nothing counted" is distinguishable from "reset". One
    /// sweep looks at both, so once the abandoned row is back the live one has been passed over.
    /// </summary>
    [Fact]
    public async Task Work_held_past_its_lease_is_released_with_nothing_counted_and_delivered_while_a_live_claim_is_left_alone()
    {
        var abandonedId = 0;
        var liveId = 0;
        var liveReader = 0;
        var liveClaimedAt = DateTime.UtcNow;
        await ArrangeAsync(async context =>
        {
            var crashedReader = NewReader("10.0.7.1");
            var busyReader = NewReader("10.0.7.2");
            var spectator = NewSpectator("TICKET-1", "100001");
            context.AddRange(crashedReader, busyReader, spectator);
            await context.SaveChangesAsync();
            liveReader = busyReader.Id;

            var abandoned = Replication.Create(
                spectator.Id,
                crashedReader.Id,
                ReplicationOperation.Add,
                ReplicationLane.Live,
                Earlier
            );
            abandoned.Begin(Earlier);
            abandoned.Fail("Refused once.", Earlier);
            abandoned.Retry(Earlier);
            abandoned.Begin(LongAgo);

            var live = Replication.Create(
                spectator.Id,
                busyReader.Id,
                ReplicationOperation.Add,
                ReplicationLane.Live,
                Earlier
            );
            live.Begin(liveClaimedAt);

            context.Replications.AddRange(abandoned, live);
            await context.SaveChangesAsync();
            (abandonedId, liveId) = (abandoned.Id, live.Id);
        });

        var queue = await QueueUntilAsync(
            queue => queue.Single(work => work.Id == abandonedId).Status == ReplicationStatus.Succeeded,
            "the abandoned work has been released and delivered"
        );

        Assert.Equal(1, queue.Single(work => work.Id == abandonedId).AttemptCount);

        var live = queue.Single(work => work.Id == liveId);
        Assert.Equal(ReplicationStatus.InProgress, live.Status);
        Assert.Equal(liveClaimedAt, live.UpdatedAt, TimeSpan.FromMilliseconds(1));
        Assert.DoesNotContain(Readers.Calls, call => call.DeviceId == liveReader);
    }

    // ─── WRK-22 with WRK-20: reclaimed, but overtaken ───

    [Fact]
    public async Task Work_held_past_its_lease_that_newer_intent_overtook_ends_superseded()
    {
        var abandonedId = 0;
        var newerId = 0;
        await ArrangeAsync(async context =>
        {
            var reader = NewReader("10.0.7.1");
            var spectator = NewSpectator("TICKET-1", "100001");
            context.AddRange(reader, spectator);
            await context.SaveChangesAsync();

            var abandoned = Replication.Create(
                spectator.Id,
                reader.Id,
                ReplicationOperation.Add,
                ReplicationLane.Live,
                Earlier
            );
            abandoned.Begin(LongAgo);
            var newer = Replication.Create(
                spectator.Id,
                reader.Id,
                ReplicationOperation.Update,
                ReplicationLane.Live,
                DateTime.UtcNow
            );

            context.Replications.AddRange(abandoned, newer);
            await context.SaveChangesAsync();
            (abandonedId, newerId) = (abandoned.Id, newer.Id);
        });

        var queue = await QueueUntilAsync(
            queue => queue.Single(work => work.Id == newerId).Status == ReplicationStatus.Succeeded,
            "the newer intent has been delivered"
        );

        var abandoned = queue.Single(work => work.Id == abandonedId);
        Assert.Equal(ReplicationStatus.Superseded, abandoned.Status);
        Assert.Equal(0, abandoned.AttemptCount);
        Assert.Equal(ReplicationOperation.Update, Assert.Single(Readers.Calls).Operation);
    }

    // ─── WRK-23: the worker is stopped mid-call ───

    /// <summary>
    /// On a host of its own, because the host is what is stopped. The reader would take thirty
    /// seconds to answer; the host is shut down while it is still waiting, and nothing else is
    /// draining that database, so whatever the stop left behind is what the test reads.
    /// </summary>
    [Fact]
    public async Task Work_whose_call_was_cut_off_by_a_shutdown_goes_back_to_the_queue_with_no_attempt_counted()
    {
        var host = await Fixture.StartIsolatedHostAsync(new Dictionary<string, string?>());
        await using (host)
        {
            using var client = host.CreateClient();
            var reader = await GivenRegisteredDeviceAsync("10.0.7.1", via: client);
            host.Readers.SetLatency(reader, TimeSpan.FromSeconds(30));
            await UpsertSpectatorAsync("TICKET-1", via: client);
            await Eventually.UntilAsync(
                () => Task.FromResult(host.Readers.Calls),
                calls => calls.Count == 1,
                "the call is with the reader"
            );
            Assert.Equal(ReplicationStatus.InProgress, Assert.Single(await QueuedWorkAsync(host)).Status);
        }

        var work = Assert.Single(await QueuedWorkAsync(host));
        Assert.Equal(ReplicationStatus.Pending, work.Status);
        Assert.Equal(0, work.AttemptCount);
    }
}
