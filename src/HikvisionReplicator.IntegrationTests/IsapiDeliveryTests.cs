using System.Net;
using System.Net.Http.Json;
using HikvisionReplicator.Api.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A spectator reaching a real-protocol reader through the API (ISA-01, ISA-07, ISA-10, ISA-21,
/// ISA-26): a worker host of its own on the ISAPI adapter, a <see cref="StubReader"/> registered
/// at its loopback address, and users driven through their routes. What the reader holds is
/// read off the stub, and the queue off the host's database (AD-036).
/// </summary>
[Collection(WorkerCollectionDefinition.Name)]
public class IsapiDeliveryTests(WorkerPostgresFixture fixture) : WorkerApiTests(fixture)
{
    /// <summary>The A-2 key of <c>TICKET-0001</c>, computed outside .NET (see IsapiIdentityTests).</summary>
    private const string Ticket1EmployeeNo = "b43a95a2c19a97b6289861e47299284f";

    private static bool Settled(List<Replication> queue, ReplicationOperation operation) =>
        queue.Any(work => work.Operation == operation && work.Status == ReplicationStatus.Succeeded);

    // ─── ISA-01, ISA-21: enrolled over HTTPS and digest, under the hashed key ───

    [Fact]
    public async Task A_new_spectator_is_enrolled_on_the_reader_with_name_pin_and_face()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture);

        await bench.UpsertAsync("TICKET-0001", "Ana Souza", "123456");

        var queue = await bench.QueueUntilAsync(
            queue => Settled(queue, ReplicationOperation.Add),
            "the enrolment has been delivered"
        );
        var work = Assert.Single(queue);
        Assert.Equal(0, work.AttemptCount);
        Assert.Null(work.LastError);

        var person = Assert.Single(bench.Reader.Persons);
        Assert.Equal(new StubPerson(Ticket1EmployeeNo, "Ana Souza", "123456"), person.Value);
        var face = Assert.Single(bench.Reader.Faces);
        Assert.Equal(Ticket1EmployeeNo, face.Key);
        Assert.Equal(await bench.StoredPictureAsync("TICKET-0001"), face.Value);
        Assert.Equal(0, bench.Reader.FailedLogins);
    }

    // ─── ISA-07: name, PIN and face replaced ───

    [Fact]
    public async Task An_amended_spectator_has_name_pin_and_face_replaced_on_the_reader()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture);
        await bench.UpsertAsync("TICKET-0001", "Ana Souza", "123456");
        await bench.QueueUntilAsync(
            queue => Settled(queue, ReplicationOperation.Add),
            "the enrolment has been delivered"
        );
        var firstFace = bench.Reader.Faces[Ticket1EmployeeNo];

        await bench.UpsertAsync("TICKET-0001", "Ana Souza Lima", "654321", FaceFixtures.Progressive);

        await bench.QueueUntilAsync(
            queue => Settled(queue, ReplicationOperation.Update),
            "the amendment has been delivered"
        );
        Assert.Equal(
            new StubPerson(Ticket1EmployeeNo, "Ana Souza Lima", "654321"),
            Assert.Single(bench.Reader.Persons).Value
        );
        var face = Assert.Single(bench.Reader.Faces);
        Assert.Equal(await bench.StoredPictureAsync("TICKET-0001"), face.Value);
        Assert.NotEqual(firstFace, face.Value);
    }

    // ─── ISA-10: face and person both gone ───

    [Fact]
    public async Task A_removed_spectator_leaves_neither_person_nor_face_on_the_reader()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture);
        await bench.UpsertAsync("TICKET-0001", "Ana Souza", "123456");
        await bench.QueueUntilAsync(
            queue => Settled(queue, ReplicationOperation.Add),
            "the enrolment has been delivered"
        );

        await bench.RemoveAsync("TICKET-0001");

        await bench.QueueUntilAsync(
            queue => Settled(queue, ReplicationOperation.Remove),
            "the removal has been delivered"
        );
        Assert.Empty(bench.Reader.Persons);
        Assert.Empty(bench.Reader.Faces);
    }

    // ─── ISA-26: a long name is fitted, never refused ───

    [Fact]
    public async Task A_name_longer_than_the_reader_holds_arrives_truncated_rather_than_refused()
    {
        await using var bench = await IsapiBench.StartAsync(Fixture);

        // 43 characters against the default limit of 32.
        await bench.UpsertAsync("TICKET-0001", "Maria Aparecida dos Santos Oliveira Pereira", "123456");

        var work = Assert.Single(
            await bench.QueueUntilAsync(
                queue => Settled(queue, ReplicationOperation.Add),
                "the enrolment has been delivered"
            )
        );
        Assert.Equal(0, work.AttemptCount);
        Assert.Equal("Maria Aparecida dos Santos Olive", bench.Reader.Persons[Ticket1EmployeeNo].Name);
    }
}

/// <summary>
/// One isolated worker host on the ISAPI adapter with one <see cref="StubReader"/> registered
/// through the API — what every ISAPI end-to-end test stands on. The host has a database of its
/// own, so nothing else drains its queue.
/// </summary>
internal sealed class IsapiBench : IAsyncDisposable
{
    public const string ReaderPassword = "Reader-Passw0rd-71c4";

    private IsapiBench(WorkerWebApplicationFactory host, HttpClient api, StubReader reader)
    {
        Host = host;
        Api = api;
        Reader = reader;
    }

    public WorkerWebApplicationFactory Host { get; }

    public HttpClient Api { get; }

    public StubReader Reader { get; }

    public int DeviceId { get; private set; }

    /// <summary>
    /// Starts the stub and the host, then registers the stub with
    /// <paramref name="devicePassword"/> — the reader's own password unless a test wants a
    /// wrong one.
    /// </summary>
    public static async Task<IsapiBench> StartAsync(
        WorkerPostgresFixture fixture,
        string devicePassword = ReaderPassword,
        IReadOnlyDictionary<string, string?>? settings = null,
        Action<IWebHostBuilder>? configure = null
    )
    {
        ArgumentNullException.ThrowIfNull(fixture);

        var hostSettings = StubReader.HostSettings();
        foreach (var (key, value) in settings ?? new Dictionary<string, string?>())
            hostSettings[key] = value;

        var reader = await StubReader.StartAsync(ReaderPassword);
        var host = await fixture.StartIsolatedHostAsync(hostSettings, configure);
        var bench = new IsapiBench(host, host.CreateClient(), reader);
        bench.DeviceId = await reader.RegisterThroughAsync(bench.Api, devicePassword);
        return bench;
    }

    public async Task UpsertAsync(
        string externalRef,
        string name,
        string accessCode,
        string face = FaceFixtures.Portrait
    )
    {
        using var response = await Api.PutAsJsonAsync(
            $"/api/users/{Uri.EscapeDataString(externalRef)}",
            new
            {
                name,
                accessCode,
                facePicture = FaceFixtures.Bytes(face),
            }
        );

        Assert.True(response.IsSuccessStatusCode, $"Upsert answered {response.StatusCode}.");
    }

    public async Task RemoveAsync(string externalRef)
    {
        using var response = await Api.DeleteAsync($"/api/users/{Uri.EscapeDataString(externalRef)}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>Gives the registered device a new password, through its own route.</summary>
    public async Task ChangeDevicePasswordAsync(string password)
    {
        using var response = await Api.PutAsJsonAsync($"/api/devices/{DeviceId}", new { password });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    public async Task<List<Replication>> QueueAsync()
    {
        await using var context = Host.CreateDbContext();
        return await context.Replications.OrderBy(work => work.Id).ToListAsync();
    }

    public Task<List<Replication>> QueueUntilAsync(
        Func<List<Replication>, bool> done,
        string because,
        TimeSpan? within = null
    ) => Eventually.UntilAsync(QueueAsync, done, because, within);

    /// <summary>The normalized face as stored — what the reader must hold (WRK-02).</summary>
    public async Task<byte[]> StoredPictureAsync(string externalRef)
    {
        await using var context = Host.CreateDbContext();
        var reference = ExternalRef.Create(externalRef).AsT0;
        var user = await context.Users.SingleAsync(user => user.ExternalRef == reference);
        return (
            await context.Set<FacePicture>().SingleAsync(picture => picture.UserId == user.Id)
        ).Content;
    }

    public async ValueTask DisposeAsync()
    {
        Api.Dispose();
        await Host.DisposeAsync();
        await Reader.DisposeAsync();
    }
}
