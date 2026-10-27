using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using HikvisionReplicator.Api.Domain;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The catalogue changing under a running worker (WRK-10, WRK-24, WRK-25, WRK-33): a reader that
/// joins is served without a restart, a reader that leaves is called no more — and whatever was
/// in flight to it is let go without disturbing anyone else — and a reader whose address or
/// password an operator corrects is reached at the new one on the very next call.
/// <para>
/// Every change is made through the device routes, as an operator makes it, and observed on the
/// calls the fake fleet received.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class DeviceChurnTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    // ─── WRK-10: joining ───

    [Fact]
    public async Task A_reader_registered_while_the_worker_runs_is_served_without_a_restart()
    {
        var first = await GivenRegisteredDeviceAsync("10.0.2.1");
        await UpsertSpectatorAsync("TICKET-1", "100001");
        await CallsUntilAsync(
            calls => calls.Any(call => call.DeviceId == first),
            "the worker is serving the first reader"
        );

        var joined = await GivenRegisteredDeviceAsync("10.0.2.2");
        await UpsertSpectatorAsync("TICKET-2", "100002");

        // The joined reader is also owed the spectator registered before it, by its backfill,
        // so the call that proves it is served is picked out by reference.
        var calls = await CallsUntilAsync(
            calls => calls.Any(call => call.DeviceId == joined && call.ExternalRef == "TICKET-2"),
            "the reader that joined has been called",
            within: TimeSpan.FromSeconds(2)
        );
        Assert.Equal(
            ReplicationOperation.Add,
            Assert.Single(calls, call => call.DeviceId == joined && call.ExternalRef == "TICKET-2")
                .Operation
        );
    }

    // ─── WRK-10 / WRK-25: leaving ───

    /// <summary>
    /// The reader has a backlog and answers slowly, so its loop is mid-drain when the operator
    /// deletes it. A call already on the wire may still finish; none may start once the loop
    /// has seen the delete (design § Risks, WRK-25's interpretation).
    /// <para>
    /// The delete returning is not the moment the loop sees it: the item the loop already holds
    /// may reach the reader just after the commit, because the reload that would refuse it ran
    /// a moment earlier. So <b>one</b> call after the delete is the accepted window, for that
    /// held item; a second would mean the loop took new work from a reader that is gone.
    /// </para>
    /// </summary>
    [Fact]
    public async Task A_deleted_reader_is_called_no_more()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.2.1");
        Readers.SetLatency(reader, TimeSpan.FromMilliseconds(100));
        for (var index = 0; index < 10; index++)
            await UpsertSpectatorAsync($"TICKET-{index}", $"{100_000 + index}");
        await CallsUntilAsync(
            calls => calls.Count(call => call.DeviceId == reader) >= 2,
            "the reader is mid-drain"
        );

        var removal = await Client.DeleteAsync($"/api/devices/{reader}");
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
        var deletedAt = Clock.GetUtcNow();

        // Long enough for several more calls at this reader's pace, had any been made.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        Assert.True(
            Readers.Calls.Count(call => call.DeviceId == reader && call.At > deletedAt) <= 1,
            "the reader was called again for work taken after its delete"
        );
    }

    // ─── WRK-24: deleted with a call out ───

    private const string WorkerCategory = "HikvisionReplicator.Api.Infrastructure.Worker.";

    /// <summary>
    /// What the worker logged as a fault about one reader. The worker's lines name the reader,
    /// and the host's sink is shared by every test, so the reader's id is what makes the
    /// lines this test's own.
    /// </summary>
    private List<string> WorkerErrorsAbout(int reader)
    {
        var aboutReader = new Regex($@"\bdevice {reader}\b", RegexOptions.IgnoreCase);
        return
        [
            .. Fixture.Factory.Logs.Lines.Where(line =>
                line.StartsWith(WorkerCategory, StringComparison.Ordinal)
                && line.Contains("[Error]", StringComparison.Ordinal)
                && aboutReader.IsMatch(line)
            ),
        ];
    }

    private async Task GivenDeliveredAsync(string externalRef, int reader)
    {
        var spectator = await StoredUserAsync(externalRef);
        await QueueUntilAsync(
            queue =>
                queue.Any(work =>
                    work.DeviceId == reader
                    && work.UserId == spectator.Id
                    && work.Status == ReplicationStatus.Succeeded
                ),
            $"{externalRef} has been delivered to reader {reader}"
        );
    }

    [Fact]
    public async Task A_reader_deleted_with_a_call_out_leaves_the_worker_delivering_to_the_others()
    {
        var deleted = await GivenRegisteredDeviceAsync("10.0.2.1");
        var other = await GivenRegisteredDeviceAsync("10.0.2.2");
        Readers.SetLatency(deleted, TimeSpan.FromSeconds(2));
        await UpsertSpectatorAsync("TICKET-1", "100001");
        await CallsUntilAsync(
            calls => calls.Any(call => call.DeviceId == deleted),
            "the call to the doomed reader is out"
        );

        var removal = await Client.DeleteAsync($"/api/devices/{deleted}");
        Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);

        await UpsertSpectatorAsync("TICKET-2", "100002");
        await GivenDeliveredAsync("TICKET-2", other);

        Assert.Empty(WorkerErrorsAbout(deleted));
        var worker = Fixture
            .Factory.Services.GetServices<IHostedService>()
            .OfType<Api.Infrastructure.Worker.ReplicationWorker>()
            .Single();
        Assert.False(worker.ExecuteTask!.IsCompleted);
    }

    /// <summary>
    /// The reader answers, and is deleted in the instant before its answer is saved — from
    /// inside that save, so the row is gone by the time the save reaches the database every
    /// run. The save then affects no row, and the outcome has nowhere to go: it is let go and
    /// noted, and nothing about it is a fault.
    /// </summary>
    [Fact]
    public async Task The_answer_of_a_reader_deleted_before_it_was_saved_is_discarded_without_a_fault()
    {
        var deleted = await GivenRegisteredDeviceAsync("10.0.2.1");
        var other = await GivenRegisteredDeviceAsync("10.0.2.2");
        var discarded = 0;
        Fixture.Factory.Saves.BeforeCommit(async move =>
        {
            if (
                move is { From: ReplicationStatus.InProgress, To: ReplicationStatus.Succeeded }
                && move.DeviceId == deleted
                && Interlocked.CompareExchange(ref discarded, move.Id, 0) == 0
            )
            {
                var removal = await Client.DeleteAsync($"/api/devices/{deleted}");
                Assert.Equal(HttpStatusCode.NoContent, removal.StatusCode);
            }
        });

        await UpsertSpectatorAsync("TICKET-1", "100001");

        await Eventually.UntilAsync(
            () => Task.FromResult(Fixture.Factory.Logs.Lines),
            lines =>
                discarded != 0
                && lines.Any(line =>
                    line.StartsWith(WorkerCategory, StringComparison.Ordinal)
                    && line.Contains($"Replication {discarded} ended Delivered", StringComparison.Ordinal)
                    && line.Contains("discarded", StringComparison.Ordinal)
                ),
            "the outcome has been let go"
        );

        await UpsertSpectatorAsync("TICKET-2", "100002");
        await GivenDeliveredAsync("TICKET-2", other);

        Assert.Empty(WorkerErrorsAbout(deleted));
        Assert.DoesNotContain(await QueuedWorkAsync(), work => work.DeviceId == deleted);
    }

    // ─── WRK-33: changing ───

    [Fact]
    public async Task A_reader_whose_address_and_password_changed_is_reached_at_the_new_ones()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.2.1");
        await UpsertSpectatorAsync("TICKET-1", "100001");
        var before = Assert.Single(
            await CallsUntilAsync(calls => calls.Count == 1, "the first spectator is delivered")
        );
        Assert.Equal("10.0.2.1", before.Endpoint.IpAddress);
        Assert.Equal(DevicePassword, before.Endpoint.Password);

        var update = await Client.PutAsJsonAsync(
            $"/api/devices/{reader}",
            new { ipAddress = "10.0.2.99", password = "n3w-Passw0rd" }
        );
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);

        await UpsertSpectatorAsync("TICKET-2", "100002");

        var after = (
            await CallsUntilAsync(calls => calls.Count == 2, "the second spectator is delivered")
        )[^1];
        Assert.Equal("TICKET-2", after.ExternalRef);
        Assert.Equal(ReplicationOperation.Add, after.Operation);
        Assert.Equal("10.0.2.99", after.Endpoint.IpAddress);
        Assert.Equal("n3w-Passw0rd", after.Endpoint.Password);
        Assert.Equal(80, after.Endpoint.Port);
    }
}
