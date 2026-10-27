using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A reader that answers and refuses a record (WRK-15…WRK-19): the record waits out a doubling
/// backoff with the refusal counted and kept, is set aside as a dead letter at the fifth, and
/// never holds up the reader or the spectator's later intent. An adapter that throws is treated
/// the same way, so a bug shows up as dead letters rather than as a reader paused for ever.
/// <para>
/// Where a backoff has to be held still to be observed, the clock is frozen: a rejected row's
/// not-before time is computed from the frozen instant, so no amount of real time makes it
/// eligible until the test moves the clock.
/// </para>
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class RejectionTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    private static readonly DateTimeOffset Kickoff = new(2026, 10, 26, 18, 45, 0, TimeSpan.Zero);

    /// <summary>The harness's first backoff: <c>RetryBaseDelay</c> × 2⁰.</summary>
    private static readonly TimeSpan FirstBackoff = TimeSpan.FromMilliseconds(100);

    /// <summary>The harness's longest backoff, <c>RetryMaxDelay</c>.</summary>
    private static readonly TimeSpan LongestBackoff = TimeSpan.FromMilliseconds(400);

    private const int MaxAttempts = 5;

    private static string RefusalOf(int reader, string externalRef) =>
        $"Device {reader} refused {externalRef}.";

    private async Task<Replication> WorkForAsync(string externalRef, ReplicationOperation operation)
    {
        var user = await StoredUserAsync(externalRef);
        return (await QueuedWorkAsync()).Single(work =>
            work.UserId == user.Id && work.Operation == operation
        );
    }

    private async Task<Replication> DeadLetterAsync(string externalRef) =>
        await Eventually.UntilAsync(
            () => WorkForAsync(externalRef, ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Failed,
            $"{externalRef} has been dead-lettered"
        );

    // ─── WRK-16: refused, counted, kept, and held back ───

    [Fact]
    public async Task A_refused_record_is_requeued_with_the_refusal_counted_and_waits_out_its_backoff()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.5.1");
        Readers.RejectWhen((_, externalRef) => externalRef == "TICKET-1");
        Clock.Freeze(Kickoff);

        await UpsertSpectatorAsync("TICKET-1");

        var requeued = await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-1", ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Pending && work.AttemptCount == 1,
            "the refusal has been recorded"
        );
        Assert.Equal(RefusalOf(reader, "TICKET-1"), requeued.LastError);
        Assert.Equal((Kickoff + FirstBackoff).UtcDateTime, requeued.NextAttemptAt);

        // Absence, so a window has to pass: many polls, with the clock short of the backoff by
        // a millisecond, and the record is still not offered again.
        Clock.Freeze(Kickoff + FirstBackoff - TimeSpan.FromMilliseconds(1));
        await Task.Delay(WorkerWebApplicationFactory.PollInterval * 10);
        Assert.Single(Readers.Calls);

        Readers.RejectWhen((_, _) => false);
        Clock.Freeze(Kickoff + FirstBackoff);

        var delivered = await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-1", ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Succeeded,
            "the record has been offered again once its backoff passed"
        );
        Assert.Equal(1, delivered.AttemptCount);
        Assert.Equal(2, Readers.Calls.Count);
    }

    // ─── WRK-17: the dead letter ───

    [Fact]
    public async Task A_record_refused_every_time_is_set_aside_at_the_last_attempt_and_never_offered_again()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.5.1");
        Readers.RejectWhen((_, externalRef) => externalRef == "TICKET-1");

        await UpsertSpectatorAsync("TICKET-1");

        var deadLetter = await DeadLetterAsync("TICKET-1");
        Assert.Equal(MaxAttempts, deadLetter.AttemptCount);
        Assert.Equal(RefusalOf(reader, "TICKET-1"), deadLetter.LastError);
        Assert.Equal(MaxAttempts, Readers.Calls.Count(call => call.ExternalRef == "TICKET-1"));

        // The reader carries on with everyone else.
        await UpsertSpectatorAsync("TICKET-2", "100002");
        await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-2", ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Succeeded,
            "the next spectator has been delivered"
        );

        // Absence again: longer than the longest backoff, and the dead letter is not offered.
        await Task.Delay(LongestBackoff * 2);
        Assert.Equal(MaxAttempts, Readers.Calls.Count(call => call.ExternalRef == "TICKET-1"));
        Assert.Equal(ReplicationStatus.Failed, (await WorkForAsync("TICKET-1", ReplicationOperation.Add)).Status);
    }

    // ─── WRK-18: a newer intent after a dead letter ───

    [Fact]
    public async Task A_newer_intent_after_a_dead_letter_is_delivered_and_the_dead_letter_is_left_as_it_was()
    {
        var reader = await GivenRegisteredDeviceAsync("10.0.5.1");
        Readers.RejectWhen((_, externalRef) => externalRef == "TICKET-1");
        await UpsertSpectatorAsync("TICKET-1", "100001");
        var deadLetter = await DeadLetterAsync("TICKET-1");

        Readers.RejectWhen((_, _) => false);
        await UpsertSpectatorAsync("TICKET-1", "654321");

        var amendment = await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-1", ReplicationOperation.Update),
            work => work.Status == ReplicationStatus.Succeeded,
            "the amendment has been delivered"
        );
        Assert.Equal(0, amendment.AttemptCount);
        Assert.Equal("654321", Readers.Enrolled(reader)["TICKET-1"].AccessCode);

        var after = await WorkForAsync("TICKET-1", ReplicationOperation.Add);
        Assert.Equal(ReplicationStatus.Failed, after.Status);
        Assert.Equal(deadLetter.AttemptCount, after.AttemptCount);
        Assert.Equal(deadLetter.LastError, after.LastError);
        Assert.Equal(deadLetter.UpdatedAt, after.UpdatedAt);
    }

    // ─── WRK-19: a newer intent does not inherit the old backoff ───

    [Fact]
    public async Task A_newer_intent_overtaking_a_backoff_is_delivered_without_waiting_it_out()
    {
        await GivenRegisteredDeviceAsync("10.0.5.1");
        var refused = 0;
        Readers.RejectWhen(
            (_, externalRef) => externalRef == "TICKET-1" && Interlocked.Increment(ref refused) == 1
        );
        Clock.Freeze(Kickoff);

        await UpsertSpectatorAsync("TICKET-1", "100001");
        await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-1", ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Pending && work.AttemptCount == 1,
            "the refusal has been recorded"
        );

        // The clock stays frozen, so the old backoff never runs out.
        await UpsertSpectatorAsync("TICKET-1", "654321");

        var amendment = await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-1", ReplicationOperation.Update),
            work => work.Status == ReplicationStatus.Succeeded,
            "the amendment has been delivered while the old backoff was still running"
        );
        Assert.Equal(0, amendment.AttemptCount);

        var overtaken = await WorkForAsync("TICKET-1", ReplicationOperation.Add);
        Assert.Equal(ReplicationStatus.Superseded, overtaken.Status);
        Assert.Equal(1, overtaken.AttemptCount);
        Assert.Equal(
            [ReplicationOperation.Add, ReplicationOperation.Update],
            Readers.Calls.Select(call => call.Operation)
        );
    }

    // ─── WRK-15: a refusal never pauses the reader ───

    /// <summary>
    /// With the clock frozen a pause could never end — its probe would be due at an instant
    /// that never comes — so two refusals in a row followed by a delivery proves the refusals
    /// left the reader unpaused (two unanswered calls would have paused it).
    /// </summary>
    [Fact]
    public async Task A_reader_that_refuses_records_is_not_paused()
    {
        await GivenRegisteredDeviceAsync("10.0.5.1");
        Readers.RejectWhen((_, externalRef) => externalRef is "TICKET-1" or "TICKET-2");
        Clock.Freeze(Kickoff);

        await UpsertSpectatorAsync("TICKET-1", "100001");
        await UpsertSpectatorAsync("TICKET-2", "100002");
        await Eventually.UntilAsync(
            QueuedWorkAsync,
            queue => queue.Count == 2 && queue.All(work => work.AttemptCount == 1),
            "both refusals have been recorded"
        );

        await UpsertSpectatorAsync("TICKET-3", "100003");

        await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-3", ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Succeeded,
            "the next spectator has been delivered right after two refusals"
        );
    }

    // ─── An adapter that throws is a refusal ───

    [Fact]
    public async Task A_device_client_that_throws_is_counted_as_a_refusal_and_ends_in_a_dead_letter()
    {
        await GivenRegisteredDeviceAsync("10.0.5.1");
        Readers.RejectWhen(
            (_, externalRef) =>
                externalRef == "TICKET-1"
                    ? throw new InvalidOperationException("The adapter tripped over its own feet.")
                    : false
        );

        await UpsertSpectatorAsync("TICKET-1", "100001");

        // Counted as unreachable instead, it would be released for ever and never fail.
        var deadLetter = await DeadLetterAsync("TICKET-1");
        Assert.Equal(MaxAttempts, deadLetter.AttemptCount);
        Assert.Contains(
            "The adapter tripped over its own feet.",
            deadLetter.LastError,
            StringComparison.Ordinal
        );

        // Nor did it pause the reader.
        await UpsertSpectatorAsync("TICKET-2", "100002");
        await Eventually.UntilAsync(
            () => WorkForAsync("TICKET-2", ReplicationOperation.Add),
            work => work.Status == ReplicationStatus.Succeeded,
            "the next spectator has been delivered"
        );
    }
}
