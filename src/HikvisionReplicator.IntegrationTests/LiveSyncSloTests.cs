using System.Diagnostics;
using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// AD-038's SLO in the one situation it was written for (WRK-09): a spectator who buys a ticket
/// while a pre-event seed is still loading every reader. Their enrolment must overtake the
/// backlog — on all twenty readers, within five seconds of the upsert completing — and the
/// backlog must still be there when it does, or the test would be timing a drained queue, the
/// one reading AD-038 rejects.
/// <para>
/// The backlog is real backfill: the roster is registered through the API before any reader
/// exists, then twenty readers are registered and the worker expands each one's debt into Bulk
/// enrolments, exactly as a seed arrives in production.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class LiveSyncSloTests(WorkerPostgresFixture fixture, ITestOutputHelper output)
    : WorkerApiTests(fixture)
{
    private const int Readers20 = 20;

    /// <summary>
    /// More than the hundred WRK-09 asks for, because each reader starts draining the moment
    /// its debt is expanded: the margin is what is still left when the last one is.
    /// </summary>
    private const int Roster = 200;

    private const int MinimumBacklog = 100;

    private static readonly TimeSpan ReaderLatency = TimeSpan.FromMilliseconds(50);

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(5);

    private sealed record Row(int DeviceId, int UserId, ReplicationLane Lane, ReplicationStatus Status);

    /// <summary>The whole queue in one statement, so every count in it is of the same instant.</summary>
    private async Task<List<Row>> QueueSnapshotAsync()
    {
        await using var context = Fixture.CreateDbContext();
        return await context
            .Replications.Select(work => new Row(work.DeviceId, work.UserId, work.Lane, work.Status))
            .ToListAsync();
    }

    private static int PendingBulk(List<Row> queue, int reader) =>
        queue.Count(row =>
            row.DeviceId == reader
            && row.Lane == ReplicationLane.Bulk
            && row.Status == ReplicationStatus.Pending
        );

    [Fact]
    public async Task A_live_upsert_reaches_every_reader_within_five_seconds_while_their_backfill_is_still_pending()
    {
        // The roster, before there is any reader to owe it to.
        var seeding = Stopwatch.StartNew();
        await Parallel.ForEachAsync(
            Enumerable.Range(0, Roster),
            new ParallelOptions { MaxDegreeOfParallelism = 8 },
            async (index, _) => await UpsertSpectatorAsync($"SEASON-{index}", $"{200_000 + index}")
        );
        seeding.Stop();

        Readers.SetLatency(ReaderLatency);
        var readers = new List<int>();
        for (var index = 0; index < Readers20; index++)
            readers.Add(await GivenRegisteredDeviceAsync($"10.0.9.{index + 1}"));

        // Every reader's debt expanded — and with it, every reader draining.
        var before = await Eventually.UntilAsync(
            QueueSnapshotAsync,
            queue => readers.All(reader => queue.Count(row => row.DeviceId == reader) == Roster),
            "every reader's backfill has been expanded"
        );
        Assert.All(
            readers,
            reader =>
                Assert.True(
                    PendingBulk(before, reader) >= MinimumBacklog,
                    $"Reader {reader} had only {PendingBulk(before, reader)} Bulk rows pending "
                        + $"when the live upsert was made (seeding took {seeding.ElapsedMilliseconds} ms)."
                )
        );

        await UpsertSpectatorAsync("WALK-UP-1", "999999");
        var sinceUpsert = Stopwatch.StartNew();
        var spectator = (await StoredUserAsync("WALK-UP-1")).Id;

        var settled = await Eventually.UntilAsync(
            QueueSnapshotAsync,
            queue =>
                queue.Count(row =>
                    row.UserId == spectator
                    && row.Lane == ReplicationLane.Live
                    && row.Status == ReplicationStatus.Succeeded
                ) == Readers20,
            "the walk-up spectator has reached every reader",
            within: Bound + TimeSpan.FromSeconds(5)
        );
        sinceUpsert.Stop();

        output.WriteLine(
            $"Seeded {Roster} spectators in {seeding.ElapsedMilliseconds} ms; least Bulk pending "
                + $"at the upsert {readers.Min(reader => PendingBulk(before, reader))}; walk-up on "
                + $"all {Readers20} readers after {sinceUpsert.ElapsedMilliseconds} ms; least Bulk "
                + $"still pending then {readers.Min(reader => PendingBulk(settled, reader))}."
        );

        Assert.True(
            sinceUpsert.Elapsed <= Bound,
            $"The walk-up spectator reached every reader {sinceUpsert.ElapsedMilliseconds} ms after "
                + $"the upsert completed; the bound is {Bound.TotalMilliseconds} ms."
        );

        // At that same instant the backlog is still being worked through on every reader.
        Assert.All(
            readers,
            reader =>
                Assert.True(
                    PendingBulk(settled, reader) > 0,
                    $"Reader {reader} had drained its backfill before the walk-up arrived."
                )
        );
    }
}
