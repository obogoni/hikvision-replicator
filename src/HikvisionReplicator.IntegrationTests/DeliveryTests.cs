using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Infrastructure.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The worker draining the queue to every reader (WRK-01…WRK-05, WRK-31, WRK-32): a spectator
/// registered, amended or removed through the API reaches each reader as the matching
/// operation, carrying what the reader needs and nothing more, and each row ends
/// <c>Succeeded</c> on the injected clock.
/// <para>
/// Observed from both ends a caller cannot see: the calls the fake readers received, and the
/// rows as the database holds them. Where an order has to be chosen — Live against Bulk, or
/// oldest within a lane — the rows are arranged by hand, because no route stages Bulk work and
/// none lets a test pick a creation time.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class DeliveryTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 10, 26, 18, 45, 0, TimeSpan.Zero);

    private static readonly DateTime Earlier = new(2026, 10, 26, 18, 0, 0, DateTimeKind.Utc);

    private static Replication AddFor(
        User spectator,
        Device reader,
        ReplicationLane lane,
        DateTime createdAt
    ) => Replication.Create(spectator.Id, reader.Id, ReplicationOperation.Add, lane, createdAt);

    // ─── WRK-01 / WRK-02: every reader receives the spectator ───

    [Fact]
    public async Task A_new_spectator_is_enrolled_on_every_reader_with_their_reference_code_and_face()
    {
        var readers = new[]
        {
            await GivenRegisteredDeviceAsync("10.0.1.1"),
            await GivenRegisteredDeviceAsync("10.0.1.2"),
            await GivenRegisteredDeviceAsync("10.0.1.3"),
        };

        await UpsertSpectatorAsync("TICKET-1", "123456");

        var calls = await CallsUntilAsync(
            calls => calls.Count >= 3,
            "every reader has been called"
        );
        var face = await StoredPictureAsync("TICKET-1");

        Assert.Equal(readers.Order(), calls.Select(call => call.DeviceId).Order());
        Assert.All(
            calls,
            call =>
            {
                Assert.Equal(ReplicationOperation.Add, call.Operation);
                Assert.Equal("TICKET-1", call.Enrolment!.ExternalRef);
                Assert.Equal("123456", call.Enrolment.AccessCode);
                Assert.Equal(face, call.Enrolment.FacePicture.ToArray());
            }
        );
    }

    // ─── WRK-03: in hand before the call, succeeded after it, on the injected clock ───

    [Fact]
    public async Task Work_is_in_progress_while_its_call_is_with_the_reader()
    {
        await GivenRegisteredDeviceAsync("10.0.1.1");
        Readers.SetLatency(TimeSpan.FromSeconds(2));

        await UpsertSpectatorAsync("TICKET-1");
        await CallsUntilAsync(calls => calls.Count == 1, "the reader has been called");

        // The call is still on the wire — the reader answers after two seconds.
        Assert.Equal(ReplicationStatus.InProgress, Assert.Single(await QueuedWorkAsync()).Status);

        await QueueUntilAsync(
            queue => queue.Single().Status == ReplicationStatus.Succeeded,
            "the reader's answer has been recorded"
        );
    }

    [Fact]
    public async Task Delivered_work_is_succeeded_at_the_time_the_injected_clock_reported()
    {
        await GivenRegisteredDeviceAsync("10.0.1.1");
        await GivenRegisteredDeviceAsync("10.0.1.2");
        await GivenRegisteredDeviceAsync("10.0.1.3");
        Readers.SetLatency(TimeSpan.FromMilliseconds(500));

        Clock.Freeze(Kickoff);
        await UpsertSpectatorAsync("TICKET-1");

        // The calls take half a second, so the answers are recorded on the later instant.
        Clock.Freeze(Kickoff.AddMinutes(1));

        var queue = await QueueUntilAsync(
            queue =>
                queue.Count == 3 && queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "every reader's row has succeeded"
        );

        Assert.All(
            queue,
            work =>
            {
                Assert.Equal(Kickoff.UtcDateTime, work.CreatedAt);
                Assert.Equal(Kickoff.AddMinutes(1).UtcDateTime, work.UpdatedAt);
            }
        );
    }

    // ─── WRK-02: an amendment and a removal carry what each needs ───

    [Fact]
    public async Task An_amended_spectator_reaches_the_reader_as_an_update_with_the_new_code()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.1.1");
        await UpsertSpectatorAsync("TICKET-1", "123456");
        await QueueUntilAsync(
            queue => queue.Single().Status == ReplicationStatus.Succeeded,
            "the enrolment has been delivered"
        );

        await UpsertSpectatorAsync("TICKET-1", "654321");

        await QueueUntilAsync(
            queue => queue.Count == 2 && queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the amendment has been delivered"
        );
        var update = Readers.Calls[^1];
        Assert.Equal(reader, update.DeviceId);
        Assert.Equal(ReplicationOperation.Update, update.Operation);
        Assert.Equal("TICKET-1", update.Enrolment!.ExternalRef);
        Assert.Equal("654321", update.Enrolment.AccessCode);
        Assert.Equal(await StoredPictureAsync("TICKET-1"), update.Enrolment.FacePicture.ToArray());
    }

    [Fact]
    public async Task A_removed_spectator_is_taken_off_the_reader_by_reference_alone()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.1.1");
        await UpsertSpectatorAsync("TICKET-1");
        await QueueUntilAsync(
            queue => queue.Single().Status == ReplicationStatus.Succeeded,
            "the enrolment has been delivered"
        );

        await RemoveSpectatorAsync("TICKET-1");

        await QueueUntilAsync(
            queue => queue.Count == 2 && queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the removal has been delivered"
        );
        var removal = Readers.Calls[^1];
        Assert.Equal(reader, removal.DeviceId);
        Assert.Equal(ReplicationOperation.Remove, removal.Operation);
        Assert.Equal("TICKET-1", removal.ExternalRef);
        Assert.Null(removal.Enrolment);
        Assert.Empty(Readers.Enrolled(reader));
    }

    // ─── WRK-05: Live before Bulk; oldest first within a lane ───

    [Fact]
    public async Task Live_work_is_taken_before_older_bulk_work()
    {
        var reader = 0;
        await ArrangeAsync(async context =>
        {
            var device = NewReader("10.0.1.1");
            var backfilled = NewSpectator("BULK-1", "100001");
            var live = NewSpectator("LIVE-1", "100002");
            context.AddRange(device, backfilled, live);
            await context.SaveChangesAsync();
            reader = device.Id;

            context.Replications.AddRange(
                AddFor(backfilled, device, ReplicationLane.Bulk, Earlier),
                AddFor(live, device, ReplicationLane.Live, Earlier.AddMinutes(5))
            );
            await context.SaveChangesAsync();
        });

        var calls = await CallsUntilAsync(calls => calls.Count == 2, "both rows have been delivered");

        Assert.Equal(["LIVE-1", "BULK-1"], calls.Select(call => call.ExternalRef));
        Assert.All(calls, call => Assert.Equal(reader, call.DeviceId));
    }

    [Fact]
    public async Task Within_a_lane_the_oldest_work_is_taken_first()
    {
        await ArrangeAsync(async context =>
        {
            var device = NewReader("10.0.1.1");
            var spectators = Enumerable
                .Range(1, 4)
                .Select(index => NewSpectator($"TICKET-{index}", $"10000{index}"))
                .ToArray();
            context.Add(device);
            context.AddRange(spectators);
            await context.SaveChangesAsync();

            // Inserted newest first in each lane, so creation time and insertion order
            // disagree: an order by id would deliver them backwards.
            context.Replications.AddRange(
                AddFor(spectators[1], device, ReplicationLane.Live, Earlier.AddMinutes(4)),
                AddFor(spectators[0], device, ReplicationLane.Live, Earlier.AddMinutes(3)),
                AddFor(spectators[3], device, ReplicationLane.Bulk, Earlier.AddMinutes(1)),
                AddFor(spectators[2], device, ReplicationLane.Bulk, Earlier)
            );
            await context.SaveChangesAsync();
        });

        var calls = await CallsUntilAsync(calls => calls.Count == 4, "every row has been delivered");

        Assert.Equal(
            ["TICKET-1", "TICKET-2", "TICKET-3", "TICKET-4"],
            calls.Select(call => call.ExternalRef)
        );
    }

    // ─── WRK-06: one operation per reader, however many workers run ───

    [Fact]
    public async Task Two_workers_on_one_database_never_call_a_reader_twice_at_once()
    {
        // A second application on the same database, delivering to the same fleet, so the
        // fleet sees every call either instance makes.
        using var secondHost = Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton(Readers))
        );
        using (secondHost.CreateClient()) { }

        Readers.SetLatency(TimeSpan.FromMilliseconds(50));

        var reader = 0;
        await ArrangeAsync(async context =>
        {
            var device = NewReader("10.0.1.1");
            var spectators = Enumerable
                .Range(0, 20)
                .Select(index => NewSpectator($"TICKET-{index}", $"{100_000 + index}"))
                .ToArray();
            context.Add(device);
            context.AddRange(spectators);
            await context.SaveChangesAsync();
            reader = device.Id;

            context.Replications.AddRange(
                spectators.Select(spectator =>
                    AddFor(spectator, device, ReplicationLane.Live, Earlier)
                )
            );
            await context.SaveChangesAsync();
        });

        await QueueUntilAsync(
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "every row has been delivered"
        );

        Assert.Equal(1, Readers.MaxConcurrentCalls(reader));
        Assert.Equal(20, Readers.Calls.Count);
        Assert.Equal(20, Readers.Calls.Select(call => call.ExternalRef).Distinct().Count());
    }

    // ─── WRK-07: a slow reader holds up nobody else ───

    [Fact]
    public async Task A_fast_reader_drains_its_backlog_while_a_slow_reader_is_still_on_its_first_call()
    {
        var slow = 0;
        var fast = 0;
        await ArrangeAsync(async context =>
        {
            var slowReader = NewReader("10.0.1.1");
            var fastReader = NewReader("10.0.1.2");
            var spectators = Enumerable
                .Range(0, 5)
                .Select(index => NewSpectator($"TICKET-{index}", $"{100_000 + index}"))
                .ToArray();
            context.AddRange(slowReader, fastReader);
            context.AddRange(spectators);
            await context.SaveChangesAsync();
            (slow, fast) = (slowReader.Id, fastReader.Id);

            foreach (var spectator in spectators)
            {
                context.Replications.AddRange(
                    AddFor(spectator, slowReader, ReplicationLane.Live, Earlier),
                    AddFor(spectator, fastReader, ReplicationLane.Live, Earlier)
                );
            }
            await context.SaveChangesAsync();

            Readers.SetLatency(slowReader.Id, TimeSpan.FromSeconds(2));
            Readers.SetLatency(fastReader.Id, TimeSpan.FromMilliseconds(10));
        });

        var queue = await QueueUntilAsync(
            queue =>
                queue
                    .Where(work => work.DeviceId == fast)
                    .All(work => work.Status == ReplicationStatus.Succeeded),
            "the fast reader's backlog has drained",
            within: TimeSpan.FromSeconds(2)
        );

        Assert.DoesNotContain(
            queue,
            work => work.DeviceId == slow && work.Status == ReplicationStatus.Succeeded
        );
        Assert.Single(Readers.Calls, call => call.DeviceId == slow);
    }

    // ─── WRK-08: newer intent waits for the older one in a reader's hands ───

    /// <summary>
    /// The older row is in progress by hand — the shape another instance mid-call leaves — so
    /// nothing in this host will finish it until the test does. A second reader with work of
    /// its own shows the worker running all the while.
    /// </summary>
    [Fact]
    public async Task Newer_work_for_a_pair_waits_until_the_older_work_in_hand_is_finished()
    {
        var blocked = 0;
        var olderId = 0;
        await ArrangeAsync(async context =>
        {
            var blockedReader = NewReader("10.0.1.1");
            var otherReader = NewReader("10.0.1.2");
            var spectator = NewSpectator("TICKET-1", "100001");
            context.AddRange(blockedReader, otherReader, spectator);
            await context.SaveChangesAsync();
            blocked = blockedReader.Id;

            var older = AddFor(spectator, blockedReader, ReplicationLane.Live, Earlier);
            // Taken in hand now: another instance mid-call holds a live lease. Taken at
            // Earlier, it would read as work a crashed worker abandoned (WRK-22).
            older.Begin(DateTime.UtcNow);
            context.Replications.AddRange(
                older,
                Replication.Create(
                    spectator.Id,
                    blockedReader.Id,
                    ReplicationOperation.Update,
                    ReplicationLane.Live,
                    Earlier.AddMinutes(1)
                ),
                AddFor(spectator, otherReader, ReplicationLane.Live, Earlier)
            );
            await context.SaveChangesAsync();
            olderId = older.Id;
        });

        await QueueUntilAsync(
            queue =>
                queue.Any(work =>
                    work.DeviceId != blocked && work.Status == ReplicationStatus.Succeeded
                ),
            "the other reader's work has been delivered"
        );
        await Task.Delay(WorkerWebApplicationFactory.PollInterval * 10);

        Assert.DoesNotContain(Readers.Calls, call => call.DeviceId == blocked);

        // The other instance finishes its call.
        var finishedAt = Clock.GetUtcNow();
        await using (var context = Fixture.CreateDbContext())
        {
            var older = await context.Replications.FindAsync(olderId);
            older!.Succeed(finishedAt.UtcDateTime);
            await context.SaveChangesAsync();
        }

        var calls = await CallsUntilAsync(
            calls => calls.Any(call => call.DeviceId == blocked),
            "the newer work has been delivered"
        );
        var newer = Assert.Single(calls, call => call.DeviceId == blocked);
        Assert.Equal(ReplicationOperation.Update, newer.Operation);
        Assert.True(newer.At >= finishedAt);
    }

    // ─── WRK-31 / WRK-32: nothing to do means no call at all ───

    /// <summary>
    /// Absence is proved against a reader the worker demonstrably serves: both readers join the
    /// catalogue in one transaction, so by the time the busy one has been called the idle one's
    /// loop has been looking at an empty queue too.
    /// </summary>
    [Fact]
    public async Task A_reader_with_nothing_queued_is_never_called()
    {
        var idle = 0;
        var busy = 0;
        await ArrangeAsync(async context =>
        {
            var idleReader = NewReader("10.0.1.1");
            var busyReader = NewReader("10.0.1.2");
            var spectator = NewSpectator("TICKET-1", "100001");
            context.AddRange(idleReader, busyReader, spectator);
            await context.SaveChangesAsync();
            (idle, busy) = (idleReader.Id, busyReader.Id);

            context.Replications.Add(
                AddFor(spectator, busyReader, ReplicationLane.Live, Earlier)
            );
            await context.SaveChangesAsync();
        });

        await QueueUntilAsync(
            queue => queue.Single().Status == ReplicationStatus.Succeeded,
            "the busy reader's work has been delivered"
        );

        // Ten more idle polls of the empty queue, for good measure.
        await Task.Delay(WorkerWebApplicationFactory.PollInterval * 10);

        Assert.DoesNotContain(Readers.Calls, call => call.DeviceId == idle);
        Assert.Equal(busy, Assert.Single(Readers.Calls).DeviceId);
    }

    /// <summary>
    /// The one test here that has to watch a window go by: with no reader at all there is
    /// nothing whose delivery could mark the worker as having looked. The window is many
    /// polls long, and the assertion is that the worker is still running and called no one.
    /// </summary>
    [Fact]
    public async Task With_no_readers_the_worker_keeps_running_and_calls_nobody()
    {
        await UpsertSpectatorAsync("TICKET-1");

        await Task.Delay(WorkerWebApplicationFactory.PollInterval * 10);

        var worker = Fixture
            .Factory.Services.GetServices<IHostedService>()
            .OfType<ReplicationWorker>()
            .Single();
        Assert.False(worker.ExecuteTask!.IsCompleted);
        Assert.Empty(Readers.Calls);
        Assert.Empty(await QueuedWorkAsync());
    }
}
