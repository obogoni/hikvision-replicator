using System.Net;
using System.Net.Http.Json;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The catalogue changing under a running worker (WRK-10, WRK-25, WRK-33): a reader that joins
/// is served without a restart, a reader that leaves is called no more, and a reader whose
/// address or password an operator corrects is reached at the new one on the very next call.
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
    /// deletes it. A call already on the wire may still finish; none may start once the delete
    /// has committed (design § Risks, WRK-25's interpretation).
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

        Assert.DoesNotContain(
            Readers.Calls,
            call => call.DeviceId == reader && call.At > deletedAt
        );
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
