using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Time.Testing;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// The fake reader fleet every worker test scripts (design § FakeDeviceClient). If its scripting
/// lies — a latency that never elapses, an offline reader that answers, a log out of order —
/// every worker test built on it proves nothing, so the fake is pinned here first, on a clock
/// the test drives.
/// </summary>
public class FakeDeviceClientTests
{
    private static readonly DeviceEndpoint FrontGate = new(1, "10.0.0.1", 80, "admin", "secret");
    private static readonly DeviceEndpoint BackGate = new(2, "10.0.0.2", 80, "admin", "secret");

    private static DeviceEnrolment Spectator(string externalRef) =>
        new(externalRef, "123456", new byte[] { 0xFF, 0xD8, 0xFF });

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 10, 26, 18, 0, 0, TimeSpan.Zero)
    );

    private FakeDeviceClient NewFleet() => new(_clock);

    // ─── Latency: honoured on the injected clock, and cancellable ───

    [Fact]
    public async Task A_call_is_not_answered_before_its_latency_has_passed()
    {
        var fleet = NewFleet();
        fleet.SetLatency(TimeSpan.FromSeconds(5));

        var call = fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);

        _clock.Advance(TimeSpan.FromSeconds(4.9));
        Assert.False(call.IsCompleted);

        _clock.Advance(TimeSpan.FromSeconds(0.1));
        Assert.True((await call).IsT0);
    }

    [Fact]
    public async Task A_call_still_waiting_on_its_latency_is_abandoned_when_cancelled()
    {
        var fleet = NewFleet();
        fleet.SetLatency(TimeSpan.FromSeconds(10));
        using var timeout = new CancellationTokenSource();

        var call = fleet.AddAsync(FrontGate, Spectator("TICKET-1"), timeout.Token);
        await timeout.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        Assert.Empty(fleet.Enrolled(FrontGate.DeviceId));
    }

    [Fact]
    public async Task One_reader_can_be_slower_than_the_rest()
    {
        var fleet = NewFleet();
        fleet.SetLatency(BackGate.DeviceId, TimeSpan.FromSeconds(2));

        var fast = fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);
        var slow = fleet.AddAsync(BackGate, Spectator("TICKET-1"), CancellationToken.None);

        Assert.True((await fast).IsT0);
        Assert.False(slow.IsCompleted);

        _clock.Advance(TimeSpan.FromSeconds(2));
        Assert.True((await slow).IsT0);
    }

    // ─── Scripted failures ───

    [Fact]
    public async Task An_unreachable_reader_reports_unreachable_and_holds_nothing_until_restored()
    {
        var fleet = NewFleet();
        fleet.SetUnreachable(FrontGate.DeviceId, true);

        var offline = await fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);
        var otherReader = await fleet.AddAsync(BackGate, Spectator("TICKET-1"), CancellationToken.None);

        Assert.True(offline.IsT2);
        Assert.True(otherReader.IsT0);
        Assert.Empty(fleet.Enrolled(FrontGate.DeviceId));

        fleet.SetUnreachable(FrontGate.DeviceId, false);
        var restored = await fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);

        Assert.True(restored.IsT0);
        Assert.Contains("TICKET-1", fleet.Enrolled(FrontGate.DeviceId).Keys);
    }

    [Fact]
    public async Task A_reader_refuses_exactly_the_records_its_rejection_rule_names()
    {
        var fleet = NewFleet();
        fleet.RejectWhen((deviceId, externalRef) =>
            deviceId == FrontGate.DeviceId && externalRef == "BAD-FACE"
        );

        var refused = await fleet.AddAsync(FrontGate, Spectator("BAD-FACE"), CancellationToken.None);
        var accepted = await fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);
        var elsewhere = await fleet.AddAsync(BackGate, Spectator("BAD-FACE"), CancellationToken.None);

        Assert.True(refused.IsT1);
        Assert.True(accepted.IsT0);
        Assert.True(elsewhere.IsT0);
        Assert.Equal(["TICKET-1"], fleet.Enrolled(FrontGate.DeviceId).Keys);
    }

    // ─── What a reader holds ───

    [Fact]
    public async Task A_delivered_removal_takes_the_spectator_off_the_reader()
    {
        var fleet = NewFleet();
        await fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);
        await fleet.AddAsync(FrontGate, Spectator("TICKET-2"), CancellationToken.None);

        await fleet.RemoveAsync(FrontGate, "TICKET-1", CancellationToken.None);

        Assert.Equal(["TICKET-2"], fleet.Enrolled(FrontGate.DeviceId).Keys);
    }

    // ─── The call log ───

    [Fact]
    public async Task Every_call_is_logged_in_arrival_order_with_what_it_carried()
    {
        var fleet = NewFleet();
        var first = _clock.GetUtcNow();

        await fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await fleet.UpdateAsync(BackGate, Spectator("TICKET-1"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromSeconds(1));
        await fleet.RemoveAsync(FrontGate, "TICKET-1", CancellationToken.None);

        var calls = fleet.Calls;
        Assert.Equal(
            [
                (1, ReplicationOperation.Add, "TICKET-1", first),
                (2, ReplicationOperation.Update, "TICKET-1", first.AddSeconds(1)),
                (1, ReplicationOperation.Remove, "TICKET-1", first.AddSeconds(2)),
            ],
            calls.Select(call => (call.DeviceId, call.Operation, call.ExternalRef, call.At))
        );
        Assert.Equal("123456", calls[0].Enrolment!.AccessCode);
        Assert.Null(calls[2].Enrolment);
        Assert.Equal(BackGate, calls[1].Endpoint);
    }

    [Fact]
    public async Task A_call_to_an_unreachable_reader_is_still_logged()
    {
        var fleet = NewFleet();
        fleet.SetUnreachable(FrontGate.DeviceId, true);

        await fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None);

        Assert.Equal(FrontGate.DeviceId, Assert.Single(fleet.Calls).DeviceId);
    }

    // ─── The per-reader concurrency high-water mark (WRK-06) ───

    [Fact]
    public async Task Overlapping_calls_on_one_reader_raise_its_high_water_mark_and_no_other()
    {
        var fleet = NewFleet();
        fleet.SetLatency(TimeSpan.FromSeconds(1));

        var overlapping = new[]
        {
            fleet.AddAsync(FrontGate, Spectator("TICKET-1"), CancellationToken.None),
            fleet.AddAsync(FrontGate, Spectator("TICKET-2"), CancellationToken.None),
        };
        _clock.Advance(TimeSpan.FromSeconds(1));
        await Task.WhenAll(overlapping);

        for (var index = 0; index < 3; index++)
        {
            var serial = fleet.AddAsync(BackGate, Spectator($"TICKET-{index}"), CancellationToken.None);
            _clock.Advance(TimeSpan.FromSeconds(1));
            await serial;
        }

        Assert.Equal(2, fleet.MaxConcurrentCalls(FrontGate.DeviceId));
        Assert.Equal(1, fleet.MaxConcurrentCalls(BackGate.DeviceId));
    }
}
