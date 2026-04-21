namespace HikvisionReplicator.Api.Features.Users.SyncUser;

public class UserSyncJob(ILogger<UserSyncJob> logger)
{
    public void Execute(int userId, bool wasCreated)
    {
        var action = wasCreated ? "created" : "updated";
        logger.LogInformation("UserSyncJob: user {UserId} was {Action} — device sync not yet implemented", userId, action);
    }
}
