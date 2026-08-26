using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.RemoveUser;

public class RemoveUserService(IUserRepository repository, TimeProvider timeProvider)
    : IRemoveUserService
{
    /// <summary>
    /// The same message the read path uses, so a removed spectator and one that never existed
    /// are indistinguishable to the caller (USR-31).
    /// </summary>
    public const string UserNotFound = "User not found.";

    /// <summary>
    /// Tombstones the spectator: the row survives so Phase 2 keeps a valid target to push a
    /// Remove at, while the biometric is destroyed at the moment of deletion (A-5).
    /// </summary>
    public async Task<OneOf<Success, NotFoundError>> ExecuteAsync(
        string? externalRef,
        CancellationToken cancellationToken
    )
    {
        var refResult = ExternalRef.Create(externalRef);
        if (refResult.TryPickT1(out _, out var reference))
            return new NotFoundError(UserNotFound);

        // Tombstones included, because a second DELETE must find the first one's result and
        // report the same success (A-16, USR-32). Only a reference that was never registered is
        // not found (USR-33); the active-only lookup would confuse the two.
        var user = await repository.FirstOrDefaultAsync(
            new UserByExternalRefIncludingDeletedSpec(reference),
            cancellationToken
        );
        if (user is null)
            return new NotFoundError(UserNotFound);

        if (user.DeletedAt is not null)
            return new Success();

        // The bytes are not loaded by any specification (A-1), so destroying them means asking
        // for the row first: only a picture inside the tracked graph is deleted when the
        // aggregate severs it, and then it happens in the same SaveChanges — the same
        // transaction — as the tombstone itself (USR-30).
        await repository.LoadPictureAsync(user, cancellationToken);

        user.MarkDeleted(timeProvider.GetUtcNow().UtcDateTime);

        // Saved directly rather than through SaveIfKeysFreeAsync: a removal releases an access
        // code, it never claims one, so there is no constraint here to lose a race against.
        await repository.SaveChangesAsync(cancellationToken);

        return new Success();
    }
}
