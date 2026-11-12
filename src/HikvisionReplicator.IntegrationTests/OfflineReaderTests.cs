using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A reader dropping off the network (WRK-11…WRK-14): the work it never judged goes back to the
/// queue at no cost, the reader is paused after two unanswered calls and probed on a doubling
/// delay rather than hammered, and when it answers again its backlog drains in order with
/// nothing dead-lettered.
/// <para>
/// The outage is scripted on the fake fleet; the queue is read from the database and the
/// reader's side from the fleet's call log, which is where the spacing of probes shows.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class OfflineReaderTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    /// <summary>The harness's probe schedule: 100 ms doubling to a 400 ms ceiling.</summary>
    private static readonly TimeSpan[] ProbeDelays =
    [
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(400),
    ];

    // ─── WRK-11: a call that never comes back in time ───

    /// <summary>
    /// On a host of its own, because the timeout under test is a setting: 200 ms, against a
    /// reader that takes five seconds. The probe is put ten minutes out so that, once two
    /// timeouts have paused the reader, the row sits where the release left it long enough to be
    /// read — a call left to run would instead hold it in progress for the full five seconds.
    /// </summary>
    [Fact]
    public async Task Work_whose_call_outlives_the_timeout_goes_back_to_the_queue_with_no_attempt_counted()
    {
        await using var host = await Fixture.StartIsolatedHostAsync(
            new Dictionary<string, string?>
            {
                ["ReplicationWorker:DeviceCallTimeout"] = "00:00:00.200",
                ["ReplicationWorker:ProbeInitialDelay"] = "00:10:00",
                ["ReplicationWorker:ProbeMaxDelay"] = "00:10:00",
            }
        );
        using var client = host.CreateClient();
        var reader = await GivenRegisteredDeviceAsync("10.0.4.1", via: client);
        host.Readers.SetLatency(reader, TimeSpan.FromSeconds(5));

        await UpsertSpectatorAsync("TICKET-1", via: client);

        // Two timed-out calls count as two unanswered ones, and pause the reader (WRK-12).
        await Eventually.UntilAsync(
            () => Task.FromResult(host.Readers.Calls),
            calls => calls.Count == 2,
            "the reader has been called twice"
        );
        var work = await Eventually.UntilAsync(
            () => QueuedWorkAsync(host),
            queue => queue.Single().Status == ReplicationStatus.Pending,
            "the second timeout has been recorded"
        );

        Assert.Equal(0, Assert.Single(work).AttemptCount);
        Assert.All(host.Readers.Calls, call => Assert.Equal("TICKET-1", call.ExternalRef));
        Assert.Empty(host.Readers.Enrolled(reader));
    }

    // ─── WRK-14: an outage of any length loses nothing ───

    [Fact]
    public async Task An_outage_through_several_probes_fails_nothing_and_the_backlog_drains_in_order_on_return()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.4.1");
        Readers.SetUnreachable(reader, true);
        string[] tickets = ["TICKET-0", "TICKET-1", "TICKET-2", "TICKET-3", "TICKET-4"];
        foreach (var ticket in tickets)
            await UpsertSpectatorAsync(ticket, $"10000{ticket[^1]}");

        // Two unanswered calls, then four failed probes — the last two at the ceiling.
        await CallsUntilAsync(calls => calls.Count >= 6, "the reader has been probed four times");

        var during = await QueuedWorkAsync();
        Assert.Equal(tickets.Length, during.Count);
        Assert.DoesNotContain(during, work => work.Status == ReplicationStatus.Failed);
        Assert.All(during, work => Assert.Equal(0, work.AttemptCount));

        // Every probe went out with the head of the queue: a released item keeps its place
        // (WRK-11), and the probe is made with the reader's next item (WRK-13).
        Assert.All(Readers.Calls, call => Assert.Equal("TICKET-0", call.ExternalRef));

        Readers.SetUnreachable(reader, false);

        var after = await QueueUntilAsync(
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the backlog has drained"
        );

        Assert.All(after, work => Assert.Equal(0, work.AttemptCount));
        // After the reader came back every call was answered and every item was taken once, so
        // the last five calls are the deliveries, in the order they were queued (WRK-05).
        Assert.Equal(tickets, Readers.Calls.TakeLast(tickets.Length).Select(call => call.ExternalRef));
        Assert.Equal(tickets, Readers.Enrolled(reader).Keys.Order());
    }

    // ─── WRK-12 / WRK-13: paused, then probed on a doubling delay ───

    /// <summary>
    /// The spacing is read off the fleet's own log. Each call is stamped when it starts, and the
    /// next probe is scheduled from the moment the previous one was recorded — later still — so
    /// a correctly paused reader can never show a gap shorter than its scheduled delay, while a
    /// reader that was not paused would show gaps of a database round trip.
    /// </summary>
    [Fact]
    public async Task A_paused_reader_is_probed_on_the_doubling_schedule_rather_than_called_in_a_loop()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.4.1");
        Readers.SetUnreachable(reader, true);
        await UpsertSpectatorAsync("TICKET-1");

        var calls = await CallsUntilAsync(
            calls => calls.Count >= 2 + ProbeDelays.Length,
            "the reader has been probed four times"
        );

        // calls[0] and calls[1] are the two unanswered calls that paused it; each later one is
        // a probe, due the scheduled delay after the call before it.
        for (var probe = 0; probe < ProbeDelays.Length; probe++)
        {
            var gap = calls[probe + 2].At - calls[probe + 1].At;
            Assert.True(
                gap >= ProbeDelays[probe],
                $"Probe {probe + 1} came {gap.TotalMilliseconds} ms after the call before it; "
                    + $"it was due no sooner than {ProbeDelays[probe].TotalMilliseconds} ms."
            );
        }
    }
}
