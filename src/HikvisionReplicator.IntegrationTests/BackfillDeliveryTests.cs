using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A reader registered into a populated registry is owed everyone already active (WRK-04,
/// WRK-34): the worker turns that one recorded debt into a Bulk enrolment per active spectator
/// and delivers each, skipping refunded tickets — once, however many worker instances race for
/// the debt and however a live upsert interleaves with it.
/// <para>
/// The roster is built and the reader registered through the routes; what the debt turned into
/// is read from the queue and from the calls the fake reader received.
/// </para>
/// </summary>
[Collection(WorkerCollection.Name)]
public class BackfillDeliveryTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private async Task<BackfillStatus> DebtStatusAsync(int deviceId)
    {
        await using var context = Fixture.CreateDbContext();
        return (await context.BackfillIntents.SingleAsync(intent => intent.DeviceId == deviceId))
            .Status;
    }

    // ─── WRK-04: a new reader receives the roster ───

    [Fact]
    public async Task A_new_reader_is_enrolled_with_every_active_spectator_on_the_bulk_lane()
    {
        // No reader yet, so registering these queues nothing.
        await UpsertSpectatorAsync("TICKET-1", "100001");
        await UpsertSpectatorAsync("TICKET-2", "100002");
        await UpsertSpectatorAsync("REFUNDED-1", "100003");
        await RemoveSpectatorAsync("REFUNDED-1");

        var reader = await GivenRegisteredDeviceAsync("10.0.3.1");

        var queue = await QueueUntilAsync(
            queue =>
                queue.Count == 2 && queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the roster has been delivered"
        );

        Assert.All(
            queue,
            work =>
            {
                Assert.Equal(reader, work.DeviceId);
                Assert.Equal(ReplicationLane.Bulk, work.Lane);
                Assert.Equal(ReplicationOperation.Add, work.Operation);
            }
        );
        Assert.Equal(
            ["TICKET-1", "TICKET-2"],
            Readers.Calls.Select(call => call.ExternalRef).Order()
        );
        Assert.All(Readers.Calls, call => Assert.Equal(ReplicationOperation.Add, call.Operation));
        Assert.DoesNotContain("REFUNDED-1", Readers.Enrolled(reader).Keys);
        Assert.Equal(BackfillStatus.Expanded, await DebtStatusAsync(reader));
    }

    // ─── WRK-04 across instances ───

    [Fact]
    public async Task Two_workers_expand_a_readers_debt_once_and_enrol_each_spectator_once()
    {
        using var secondHost = Fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton(Readers))
        );
        using (secondHost.CreateClient()) { }

        for (var index = 0; index < 10; index++)
            await UpsertSpectatorAsync($"TICKET-{index}", $"{100_000 + index}");

        var reader = await GivenRegisteredDeviceAsync("10.0.3.1");

        var queue = await QueueUntilAsync(
            queue =>
                queue.Count >= 10 && queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the roster has been delivered"
        );

        Assert.Equal(10, queue.Count);
        Assert.Equal(10, Readers.Calls.Count(call => call.DeviceId == reader));
        Assert.Equal(
            10,
            Readers
                .Calls.Where(call => call.DeviceId == reader)
                .Select(call => call.ExternalRef)
                .Distinct()
                .Count()
        );
        Assert.Equal(BackfillStatus.Expanded, await DebtStatusAsync(reader));
    }

    // ─── WRK-34: a live upsert racing the expansion is queued once ───

    /// <summary>
    /// The race's worst landing, arranged in one transaction: the spectator's live enrolment
    /// committed after the reader's registration and before its debt was expanded. The worker
    /// must expand before it serves the reader — a reader served first would deliver the live
    /// row out of Pending, and the expansion would queue the spectator again.
    /// </summary>
    [Fact]
    public async Task Live_work_queued_before_the_expansion_is_the_only_enrolment_the_spectator_gets()
    {
        var reader = 0;
        await ArrangeAsync(async context =>
        {
            var device = NewReader("10.0.3.1");
            var spectator = NewSpectator("TICKET-1", "100001");
            context.AddRange(device, spectator);
            await context.SaveChangesAsync();
            reader = device.Id;

            context.BackfillIntents.Add(BackfillIntent.Create(device.Id, DateTime.UtcNow));
            context.Replications.Add(
                Replication.Create(
                    spectator.Id,
                    device.Id,
                    ReplicationOperation.Add,
                    ReplicationLane.Live,
                    DateTime.UtcNow
                )
            );
            await context.SaveChangesAsync();
        });

        await Eventually.UntilAsync(
            () => DebtStatusAsync(reader),
            status => status == BackfillStatus.Expanded,
            "the reader's debt has been expanded"
        );
        var queue = await QueueUntilAsync(
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the live enrolment has been delivered"
        );

        Assert.Equal(ReplicationLane.Live, Assert.Single(queue).Lane);
        Assert.Single(Readers.Calls, call => call.ExternalRef == "TICKET-1");
    }

    /// <summary>
    /// Each round registers a reader and a spectator at the same moment, so the spectator's
    /// live fan-out and the reader's backfill expansion land in either order. Whichever way
    /// they fall, every (spectator, reader) pair is owed — and receives — one enrolment.
    /// </summary>
    [Fact]
    public async Task A_spectator_registered_while_a_reader_is_registered_is_enrolled_on_it_once()
    {
        await UpsertSpectatorAsync("TICKET-ROSTER", "199999");

        var readers = new List<int>();

        // Sequential rounds, each racing a registration against an upsert.
        for (var round = 0; round < 5; round++)
        {
            var registration = GivenRegisteredDeviceAsync($"10.0.3.{round + 1}");
            var upsert = UpsertSpectatorAsync($"TICKET-{round}", $"{100_000 + round}");
            await Task.WhenAll(registration, upsert);
            readers.Add(await registration);
        }

        await Eventually.UntilAsync(
            async () =>
            {
                await using var context = Fixture.CreateDbContext();
                return await context.BackfillIntents.AllAsync(intent =>
                    intent.Status == BackfillStatus.Expanded
                );
            },
            settled => settled,
            "every reader's debt has been expanded"
        );
        var queue = await QueueUntilAsync(
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "everything queued has been delivered"
        );

        var pairs = queue.GroupBy(work => (work.UserId, work.DeviceId)).ToList();
        Assert.All(pairs, pair => Assert.Single(pair));
        Assert.All(
            Readers.Calls.GroupBy(call => (call.DeviceId, call.ExternalRef)),
            calls => Assert.Single(calls)
        );

        // Six spectators in all, and every reader is owed every one: those registered before
        // it by its backfill, those registered after it by their own live fan-out, and the one
        // registered with it by exactly one of the two.
        var calls = Readers.Calls;
        Assert.All(
            readers,
            reader => Assert.Equal(6, calls.Count(call => call.DeviceId == reader))
        );
    }
}
