using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.RemoveUser;

/// <summary>
/// Removal is idempotent by construction: repeating it reports the same success, so an
/// integrator retry after a timeout is safe (A-16). Only a reference that was never registered
/// is a failure, which is why the only error arm is <see cref="NotFoundError"/>.
/// </summary>
public interface IRemoveUserService
{
    Task<OneOf<Success, NotFoundError>> ExecuteAsync(
        string? externalRef,
        CancellationToken cancellationToken
    );
}
