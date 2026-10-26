using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// An attempt overtaken by the spectator's newer intent is never replayed (WRK-20, WRK-21): an
/// attempt that did not land ends <c>Superseded</c> with what it cost kept on it, rather than
/// going back to the queue beside the intent that replaced it; and a spectator removed while
/// their enrolment was in hand is never enrolled at all — their face is already gone.
/// <para>
/// The newer intent arrives through the routes. Where it has to arrive at one exact instant —
/// between the recorder's look and its save, or between a claim and the call — the worker's
/// own save is the trigger (<see cref="ReplicationSaveHook"/>), so the race is landed every
/// run rather than won some runs.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class StaleIntentTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    /// <summary>Long enough for an amendment to be made through the route while a call is out.</summary>
    private static readonly TimeSpan SlowAnswer = TimeSpan.FromSeconds(1);

    private ReplicationSaveHook Saves => Fixture.Factory.Saves;

    private async Task<Replication> WorkForAsync(string externalRef, ReplicationOperation operation)
    {
        var user = await StoredUserAsync(externalRef);
        return (await QueuedWorkAsync()).Single(work =>
            work.UserId == user.Id && work.Operation == operation
        );
    }

    private Task<Replication> WorkUntilAsync(
        string externalRef,
        ReplicationOperation operation,
        ReplicationStatus status
    ) =>
        Eventually.UntilAsync(
            () => WorkForAsync(externalRef, operation),
            work => work.Status == status,
            $"{externalRef}'s {operation} is {status}"
        );

    // ─── WRK-20: overtaken while the reader did not answer ───

    [Fact]
    public async Task An_unanswered_attempt_overtaken_by_an_amendment_ends_superseded_and_only_the_amendment_is_delivered()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.6.1");
        Readers.SetLatency(reader, SlowAnswer);
        Readers.SetUnreachable(reader, true);
        await UpsertSpectatorAsync("TICKET-1", "100001");
        await CallsUntilAsync(calls => calls.Count == 1, "the enrolment is with the reader");

        await UpsertSpectatorAsync("TICKET-1", "654321");

        var overtaken = await WorkUntilAsync(
            "TICKET-1",
            ReplicationOperation.Add,
            ReplicationStatus.Superseded
        );
        Assert.Equal(0, overtaken.AttemptCount);
        Assert.Equal($"Device {reader} did not answer.", overtaken.LastError);

        Readers.SetLatency(reader, TimeSpan.Zero);
        Readers.SetUnreachable(reader, false);

        await WorkUntilAsync("TICKET-1", ReplicationOperation.Update, ReplicationStatus.Succeeded);
        Assert.Equal("654321", Readers.Enrolled(reader)["TICKET-1"].AccessCode);
        Assert.Equal(
            ReplicationStatus.Superseded,
            (await WorkForAsync("TICKET-1", ReplicationOperation.Add)).Status
        );
    }

    // ─── WRK-20: overtaken while the reader refused it ───

    [Fact]
    public async Task A_refused_attempt_overtaken_by_an_amendment_ends_superseded_with_its_refusal_counted()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.6.1");
        Readers.SetLatency(reader, SlowAnswer);
        var refused = 0;
        Readers.RejectWhen((_, _) => Interlocked.Increment(ref refused) == 1);
        await UpsertSpectatorAsync("TICKET-1", "100001");
        await CallsUntilAsync(calls => calls.Count == 1, "the enrolment is with the reader");

        await UpsertSpectatorAsync("TICKET-1", "654321");

        var overtaken = await WorkUntilAsync(
            "TICKET-1",
            ReplicationOperation.Add,
            ReplicationStatus.Superseded
        );
        Assert.Equal(1, overtaken.AttemptCount);
        Assert.Equal($"Device {reader} refused TICKET-1.", overtaken.LastError);

        Readers.SetLatency(reader, TimeSpan.Zero);
        await WorkUntilAsync("TICKET-1", ReplicationOperation.Update, ReplicationStatus.Succeeded);
        Assert.Equal("654321", Readers.Enrolled(reader)["TICKET-1"].AccessCode);
    }

    // ─── WRK-20: the newer intent lands between the look and the save ───

    /// <summary>
    /// The amendment is made from inside the worker's own save, just before the release that
    /// would return the attempt to the queue is sent — after the recorder has already looked for
    /// newer intent and found none. The pending index is what notices, and the attempt must
    /// still end superseded rather than the release failing or the row staying in hand.
    /// </summary>
    [Fact]
    public async Task An_amendment_landing_just_before_a_release_is_saved_still_supersedes_the_attempt()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.6.1");
        Readers.SetUnreachable(reader, true);
        var amended = 0;
        Saves.BeforeCommit(async move =>
        {
            if (
                move is { From: ReplicationStatus.InProgress, To: ReplicationStatus.Pending }
                && Interlocked.Exchange(ref amended, 1) == 0
            )
            {
                await UpsertSpectatorAsync("TICKET-1", "654321");
            }
        });

        await UpsertSpectatorAsync("TICKET-1", "100001");

        var overtaken = await WorkUntilAsync(
            "TICKET-1",
            ReplicationOperation.Add,
            ReplicationStatus.Superseded
        );
        Assert.Equal(0, overtaken.AttemptCount);
        Assert.Equal($"Device {reader} did not answer.", overtaken.LastError);

        Readers.SetUnreachable(reader, false);
        await WorkUntilAsync("TICKET-1", ReplicationOperation.Update, ReplicationStatus.Succeeded);
        Assert.Equal("654321", Readers.Enrolled(reader)["TICKET-1"].AccessCode);
    }

    // ─── WRK-21: removed while the enrolment was in hand ───

    /// <summary>
    /// The removal is made the moment the worker's claim on the enrolment commits, so by the
    /// time the worker reads the spectator to build the call they are tombstoned and their face
    /// destroyed (USR-32). No enrolment may reach the reader; the removal queued behind it does.
    /// </summary>
    [Fact]
    public async Task A_spectator_removed_while_their_enrolment_was_in_hand_is_never_enrolled_and_is_removed()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.6.1");
        var removed = 0;
        Saves.AfterCommit(async move =>
        {
            if (
                move is { From: ReplicationStatus.Pending, To: ReplicationStatus.InProgress }
                && Interlocked.Exchange(ref removed, 1) == 0
            )
            {
                await RemoveSpectatorAsync("TICKET-1");
            }
        });

        await UpsertSpectatorAsync("TICKET-1", "100001");

        await WorkUntilAsync("TICKET-1", ReplicationOperation.Remove, ReplicationStatus.Succeeded);

        var enrolment = await WorkForAsync("TICKET-1", ReplicationOperation.Add);
        Assert.Equal(ReplicationStatus.Superseded, enrolment.Status);
        Assert.Equal(0, enrolment.AttemptCount);
        var call = Assert.Single(Readers.Calls);
        Assert.Equal(ReplicationOperation.Remove, call.Operation);
        Assert.Equal(reader, call.DeviceId);
    }
}
