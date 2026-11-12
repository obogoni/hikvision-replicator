using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure.Isapi;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A real-protocol reader failing, and the single rule that sorts it (A-4): what would fail every
/// spectator on the reader — a wrong password, a reader off the network — pauses the reader and
/// dead-letters nothing (ISA-14, ISA-15), and refused credentials are never retried into a
/// lockout (ISA-37); what fails one spectator — a refused face — dead-letters that spectator and
/// leaves no PIN-only person behind (ISA-04). The stub analogues of the bench's ISA-34 and ISA-35.
/// <para>
/// The pause and the probes are read off the host's log: the worker writes a line when it pauses
/// a reader, and the adapter one per call it answers, including a call refused locally on a
/// held credential.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class IsapiReaderFailureTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private const string WrongPassword = "Wrong-Passw0rd-0e9d";
    private const int MaxAttempts = 5;

    private static int UnreachableCalls(IsapiBench bench) =>
        bench.Host.Logs.Lines.Count(line =>
            line.Contains($"on device {bench.DeviceId} ended Unreachable", StringComparison.Ordinal)
        );

    private static bool Paused(IsapiBench bench) =>
        bench.Host.Logs.Lines.Any(line =>
            line.Contains($"Device {bench.DeviceId} paused", StringComparison.Ordinal)
        );

    /// <summary>
    /// Two unanswered calls pause the reader, then <paramref name="probes"/> more are probes — on
    /// the harness's 100 ms doubling to 400 ms schedule.
    /// </summary>
    private static Task<int> ProbedAsync(IsapiBench bench, int probes) =>
        Eventually.UntilAsync(
            () => Task.FromResult(UnreachableCalls(bench)),
            calls => Paused(bench) && calls >= 2 + probes,
            $"the reader has been paused and probed {probes} times"
        );

    private static void AssertNothingFailedOrCounted(List<Replication> queue)
    {
        Assert.NotEmpty(queue);
        Assert.DoesNotContain(queue, work => work.Status == ReplicationStatus.Failed);
        Assert.All(queue, work => Assert.Equal(0, work.AttemptCount));
    }

    // ─── ISA-15, ISA-37: a wrong password pauses, and is tried exactly once ───

    [Fact]
    public async Task A_wrong_device_password_pauses_the_reader_and_costs_one_failed_login_however_often_it_is_probed()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture, devicePassword: WrongPassword);

        await bench.UpsertAsync("TICKET-0001", "Ana Souza", "100001");
        await bench.UpsertAsync("TICKET-0002", "Bruno Lima", "100002");

        await ProbedAsync(bench, probes: 3);

        // At least five calls answered unreachable, and one login refused by the reader: every
        // call after the first was answered from the hold without contacting it.
        Assert.Equal(1, bench.Reader.FailedLogins);
        var queue = await bench.QueueAsync();
        Assert.Equal(2, queue.Count);
        Assert.All(queue, work => Assert.Equal(ReplicationStatus.Pending, work.Status));
        AssertNothingFailedOrCounted(queue);
        Assert.Empty(bench.Reader.Persons);
    }

    [Fact]
    public async Task Correcting_the_device_password_drains_the_backlog_the_wrong_one_held_back()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture, devicePassword: WrongPassword);
        await bench.UpsertAsync("TICKET-0001", "Ana Souza", "100001");
        await bench.UpsertAsync("TICKET-0002", "Bruno Lima", "100002");
        await ProbedAsync(bench, probes: 1);

        await bench.ChangeDevicePasswordAsync(IsapiBench.ReaderPassword);

        var queue = await bench.QueueUntilAsync(
            queue => queue.Count == 2 && queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the backlog has drained"
        );
        AssertNothingFailedOrCounted(queue);
        Assert.Equal(2, bench.Reader.Persons.Count);
        Assert.Equal(2, bench.Reader.Faces.Count);
        // The new credentials were not held: the first call with them logged straight in.
        Assert.Equal(1, bench.Reader.FailedLogins);
    }

    // ─── ISA-04: a refused face is this spectator's, and is undone ───

    [Fact]
    public async Task A_face_the_reader_refuses_is_dead_lettered_and_leaves_no_person_behind()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture);
        bench.Reader.Refuse(IsapiStep.FaceCreate, "modelingFailed");

        await bench.UpsertAsync("TICKET-0001", "Ana Souza", "123456");

        var deadLetter = Assert.Single(
            await bench.QueueUntilAsync(
                queue => queue.Any(work => work.Status == ReplicationStatus.Failed),
                "the refused enrolment has been dead-lettered"
            )
        );
        Assert.Equal(MaxAttempts, deadLetter.AttemptCount);
        Assert.Equal("face: modelingFailed", deadLetter.LastError);
        Assert.Empty(bench.Reader.Persons);
        Assert.Empty(bench.Reader.Faces);
        Assert.False(Paused(bench), "A refused record must not pause the reader.");
    }

    // ─── ISA-14: a reader off the network pauses, and loses nothing ───

    [Fact]
    public async Task An_offline_reader_is_paused_and_its_backlog_drains_with_nothing_dead_lettered_on_return()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture);
        bench.Reader.Offline = true;
        string[] tickets = ["TICKET-0001", "TICKET-0002", "TICKET-0003"];
        foreach (var ticket in tickets)
            await bench.UpsertAsync(ticket, "Ana Souza", $"10000{ticket[^1]}");

        await ProbedAsync(bench, probes: 2);
        var during = await bench.QueueAsync();
        Assert.Equal(tickets.Length, during.Count);
        Assert.All(during, work => Assert.Equal(ReplicationStatus.Pending, work.Status));
        AssertNothingFailedOrCounted(during);

        bench.Reader.Offline = false;

        var after = await bench.QueueUntilAsync(
            queue => queue.All(work => work.Status == ReplicationStatus.Succeeded),
            "the backlog has drained"
        );
        AssertNothingFailedOrCounted(after);
        Assert.Equal(tickets.Length, bench.Reader.Persons.Count);
        Assert.Equal(tickets.Length, bench.Reader.Faces.Count);
    }
}
