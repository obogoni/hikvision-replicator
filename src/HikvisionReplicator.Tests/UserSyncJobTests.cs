using System.Net.Http.Json;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Features.Devices.CreateDevice;
using HikvisionReplicator.Api.Features.Users.SyncUser;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using DeviceResponse = HikvisionReplicator.Api.Features.Devices.GetDevice.DeviceResponse;

namespace HikvisionReplicator.Tests;

public class UserSyncJobTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public UserSyncJobTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────

    private UserSyncJob CreateJob(IServiceScope scope)
    {
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IRepository<Device>>();
        var replicationRepo = scope.ServiceProvider.GetRequiredService<IRepository<Replication>>();
        return new UserSyncJob(NullLogger<UserSyncJob>.Instance, deviceRepo, replicationRepo);
    }

    private async Task<List<Replication>> GetReplicationsForUser(IServiceScope scope, int userId)
    {
        var repo = scope.ServiceProvider.GetRequiredService<IRepository<Replication>>();
        var all = await repo.ListAsync();
        return all.Where(r => r.UserId == userId).ToList();
    }

    private async Task<DeviceResponse> CreateDeviceAsync(string ip = "192.168.100.1", int port = 80)
    {
        var response = await _client.PostAsJsonAsync("/api/devices",
            new CreateDeviceRequest("Sync Test Device", ip, port, "admin", "secret"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<DeviceResponse>())!;
    }

    // ─── Tests ────────────────────────────────────────────────────────────

    [Fact]
    public async Task Syncing_user_creates_one_pending_add_replication_for_each_existing_device()
    {
        const int userId = 99991;

        using var scope = _factory.Services.CreateScope();
        var deviceRepo = scope.ServiceProvider.GetRequiredService<IRepository<Device>>();
        var deviceCount = (await deviceRepo.ListAsync()).Count;

        var job = CreateJob(scope);
        await job.Execute(userId, wasCreated: true);

        var replications = await GetReplicationsForUser(scope, userId);
        Assert.Equal(deviceCount, replications.Count);
        Assert.All(replications, r => Assert.Equal(ReplicationType.Add, r.Type));
        Assert.All(replications, r => Assert.Equal(ReplicationStatus.Pending, r.Status));
    }

    [Fact]
    public async Task Syncing_created_user_creates_one_pending_add_replication_per_device()
    {
        const int userId = 99992;
        var device1 = await CreateDeviceAsync("192.168.200.1", 8080);
        var device2 = await CreateDeviceAsync("192.168.200.2", 8080);

        using var scope = _factory.Services.CreateScope();
        var job = CreateJob(scope);
        await job.Execute(userId, wasCreated: true);

        var replications = await GetReplicationsForUser(scope, userId);
        Assert.Contains(replications, r => r.DeviceId == device1.Id
            && r.Type == ReplicationType.Add
            && r.Status == ReplicationStatus.Pending);
        Assert.Contains(replications, r => r.DeviceId == device2.Id
            && r.Type == ReplicationType.Add
            && r.Status == ReplicationStatus.Pending);
    }

    [Fact]
    public async Task Syncing_updated_user_also_creates_pending_add_replications()
    {
        const int userId = 99993;
        var device = await CreateDeviceAsync("192.168.201.1", 9090);

        using var scope = _factory.Services.CreateScope();
        var job = CreateJob(scope);
        await job.Execute(userId, wasCreated: false);

        var replications = await GetReplicationsForUser(scope, userId);
        Assert.Contains(replications, r => r.DeviceId == device.Id
            && r.Type == ReplicationType.Add
            && r.Status == ReplicationStatus.Pending);
    }
}
