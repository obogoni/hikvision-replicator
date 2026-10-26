using System.Data.Common;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
[Collection(WorkerCollectionDefinition.Name)]
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
    /// The race's worst landing on a host of its own: a reader owed its backfill, and a
    /// spectator whose live enrolment for it is pending, committed in one transaction.
    /// </summary>
    private async Task<int> ArrangeReaderOwedLiveWorkAsync(WorkerWebApplicationFactory host)
    {
        await using var context = host.CreateDbContext();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var device = NewReader("10.0.3.1");
        var spectator = NewSpectator("TICKET-1", "100001");
        context.AddRange(device, spectator);
        await context.SaveChangesAsync();

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
        await transaction.CommitAsync();
        return device.Id;
    }

    private static async Task<BackfillStatus> DebtStatusAsync(
        WorkerWebApplicationFactory host,
        int deviceId
    )
    {
        await using var context = host.CreateDbContext();
        return (await context.BackfillIntents.SingleAsync(intent => intent.DeviceId == deviceId))
            .Status;
    }

    /// <summary>
    /// Runs a script once, the moment the worker's first read of the outstanding debts after
    /// <see cref="Arm"/> has returned — so the script's commit lands between that read and
    /// whatever the same tick reads next. It arranges; it never asserts.
    /// </summary>
    private sealed class AfterDebtRead(Func<Task> act) : DbCommandInterceptor
    {
        private int _armed;
        private int _ran;

        public void Arm() => Volatile.Write(ref _armed, 1);

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(command);
            if (
                Volatile.Read(ref _armed) == 1
                && command.CommandText.Contains(
                    BackfillIntentConfiguration.TableName,
                    StringComparison.Ordinal
                )
                && Interlocked.Exchange(ref _ran, 1) == 0
            )
            {
                await act();
            }

            return result;
        }
    }

    /// <summary>
    /// A reader that joins the catalogue, owing its backfill and with live work pending, after
    /// the worker has read the outstanding debts and before it has read the catalogue in the
    /// same tick. Through the routes this is a registration landing in that gap; here the gap
    /// is hit on purpose. Served on that tick, the reader would take the live row out of Pending
    /// before its debt is expanded, and the expansion would enrol the spectator a second time.
    /// The host polls every half second, so a reader served early is served long before the
    /// next tick expands its debt.
    /// </summary>
    [Fact]
    public async Task A_reader_registered_while_the_worker_reads_its_debts_is_not_served_before_they_are_expanded()
    {
        WorkerWebApplicationFactory? arranging = null;
        var reader = 0;
        var landing = new AfterDebtRead(async () =>
            reader = await ArrangeReaderOwedLiveWorkAsync(arranging!)
        );
        await using var host = await Fixture.StartIsolatedHostAsync(
            new Dictionary<string, string?> { ["ReplicationWorker:PollInterval"] = "00:00:00.500" },
            builder =>
                builder.ConfigureServices(services =>
                    services.ConfigureDbContext<AppDbContext>(options =>
                        options.AddInterceptors(landing)
                    )
                )
        );
        arranging = host;
        landing.Arm();

        await Eventually.UntilAsync(
            async () => reader != 0 && await DebtStatusAsync(host, reader) == BackfillStatus.Expanded,
            expanded => expanded,
            "the reader's debt has been expanded"
        );
        var queue = await Eventually.UntilAsync(
            () => QueuedWorkAsync(host),
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the live enrolment has been delivered"
        );

        Assert.Equal(ReplicationLane.Live, Assert.Single(queue).Lane);
        Assert.Single(host.Readers.Calls, call => call.ExternalRef == "TICKET-1");
    }

    /// <summary>
    /// Refuses the first <paramref name="times"/> saves that expand a backfill, and notes what
    /// the reader had been sent by the time a later one goes through. It arranges and observes;
    /// it never asserts.
    /// </summary>
    private sealed class ExpansionsRefused(int times, Func<int> readerCalls)
        : SaveChangesInterceptor
    {
        private int _expansions;

        public int Refused { get; private set; }

        /// <summary>The calls the reader had received when its debt was finally expanded.</summary>
        public int? CallsWhenExpanded { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            ArgumentNullException.ThrowIfNull(eventData);
            var expanding =
                eventData.Context is { } context
                && context
                    .ChangeTracker.Entries<BackfillIntent>()
                    .Any(entry =>
                        entry.State == EntityState.Modified
                        && entry.Entity.Status == BackfillStatus.Expanded
                    );

            if (expanding)
            {
                if (Interlocked.Increment(ref _expansions) <= times)
                {
                    Refused++;
                    throw new InvalidOperationException("The expansion is refused.");
                }

                CallsWhenExpanded = readerCalls();
            }

            return ValueTask.FromResult(result);
        }
    }

    /// <summary>
    /// The same worst landing, with the supervisor's gate held shut on purpose: the expansion
    /// fails, so the debt is still owed on the next tick while the spectator's live row waits.
    /// The reader must get no loop — and so no call — until a later tick expands its debt; then
    /// the live row is its one enrolment. The host polls every half second, so a loop started
    /// on a failing tick would have delivered long before the retry.
    /// <para>
    /// Refused twice, not once: a tick reads the catalogue before the debts, so an arrangement
    /// committed between the two is first expanded on a tick whose catalogue does not hold the
    /// reader yet. The second refusal is on a tick that does, so the gate is always what is
    /// tested.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_reader_whose_expansion_failed_is_not_served_until_its_debt_is_expanded()
    {
        FakeDeviceClient? fleet = null;
        var hook = new ExpansionsRefused(2, () => fleet!.Calls.Count);
        await using var host = await Fixture.StartIsolatedHostAsync(
            new Dictionary<string, string?> { ["ReplicationWorker:PollInterval"] = "00:00:00.500" },
            builder =>
                builder.ConfigureServices(services =>
                    services.ConfigureDbContext<AppDbContext>(options =>
                        options.AddInterceptors(hook)
                    )
                )
        );
        fleet = host.Readers;

        var reader = await ArrangeReaderOwedLiveWorkAsync(host);

        await Eventually.UntilAsync(
            () => DebtStatusAsync(host, reader),
            status => status == BackfillStatus.Expanded,
            "the reader's debt has been expanded on a later tick"
        );
        var queue = await Eventually.UntilAsync(
            () => QueuedWorkAsync(host),
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the live enrolment has been delivered"
        );

        Assert.Equal(2, hook.Refused);
        Assert.Equal(0, hook.CallsWhenExpanded);
        Assert.Equal(ReplicationLane.Live, Assert.Single(queue).Lane);
        Assert.Single(host.Readers.Calls, call => call.ExternalRef == "TICKET-1");
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
