using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.GetUser;

public class GetUserService(IUserRepository repository) : IGetUserService
{
    /// <summary>
    /// Deliberately mentions no reference, so a reference that was never registered and one whose
    /// spectator was removed are indistinguishable to the caller (USR-31, USR-36).
    /// </summary>
    public const string UserNotFound = "User not found.";

    public async Task<OneOf<UserResponse, NotFoundError>> ExecuteAsync(
        string? externalRef,
        CancellationToken cancellationToken
    )
    {
        // A reference no spectator could ever hold is simply a reference nobody holds. Reading is
        // not a place to teach an integrator about the key format — that is the write path's job.
        var refResult = ExternalRef.Create(externalRef);
        if (refResult.TryPickT1(out _, out var reference))
            return new NotFoundError(UserNotFound);

        // Active only: a tombstoned spectator is invisible to every read path (USR-31).
        var user = await repository.FirstOrDefaultAsync(
            new UserByExternalRefSpec(reference),
            cancellationToken
        );

        return user is null ? new NotFoundError(UserNotFound) : UserResponse.FromEntity(user);
    }
}
