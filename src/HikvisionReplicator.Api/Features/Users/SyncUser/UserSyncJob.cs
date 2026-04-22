using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Features.Users.SyncUser;

public class UserSyncJob(
    ILogger<UserSyncJob> logger,
    IRepository<Device> deviceRepo,
    IRepository<Replication> replicationRepo)
{
    public async Task Execute(int userId, bool wasCreated)
    {
        var action = wasCreated ? "created" : "updated";
        logger.LogInformation("UserSyncJob: syncing user {UserId} ({Action})", userId, action);

        var devices = await deviceRepo.ListAsync();

        foreach (var device in devices)
        {
            var result = Replication.Create(userId, device.Id, ReplicationType.Add, DateTime.UtcNow);
            if (result.TryPickT1(out var error, out var replication))
                throw new InvalidOperationException(
                    $"Failed to create replication for user {userId} / device {device.Id}: {error.Field}: {error.Message}");

            await replicationRepo.AddAsync(replication);
        }

        logger.LogInformation(
            "UserSyncJob: created {Count} pending replications for user {UserId}",
            devices.Count, userId);
    }
}
