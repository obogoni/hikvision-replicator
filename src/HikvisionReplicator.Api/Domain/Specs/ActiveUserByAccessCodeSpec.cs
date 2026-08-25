using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The active spectator holding an access code. Used as the upsert pre-check, which exists only
/// to produce a friendly conflict message — the partial unique index is the authority (AD-022).
/// <para>
/// Scoped to active users on purpose: USR-06 makes an access code exclusive among active
/// spectators only, so a tombstoned holder must not block a new one (A-5). The excluded id lets a
/// spectator re-send its own code without colliding with itself.
/// </para>
/// </summary>
public sealed class ActiveUserByAccessCodeSpec : Specification<User>
{
    public ActiveUserByAccessCodeSpec(AccessCode accessCode, int excludedUserId = 0)
    {
        Query.Where(user =>
            user.AccessCode == accessCode && user.DeletedAt == null && user.Id != excludedUserId
        );
    }
}
