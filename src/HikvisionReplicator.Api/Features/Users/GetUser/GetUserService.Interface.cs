using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.GetUser;

/// <summary>
/// What the registry holds for a spectator. Carries the face picture's fingerprint and
/// <b>never its bytes</b> (USR-37): a hash, a byte size and the dimensions are everything an
/// operator at a gate — or Phase 2's change detection — needs.
/// </summary>
public record UserResponse(
    int Id,
    string ExternalRef,
    string Name,
    string AccessCode,
    string FaceContentHash,
    int FaceByteSize,
    int FaceWidth,
    int FaceHeight,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static UserResponse FromEntity(User user)
    {
        ArgumentNullException.ThrowIfNull(user);

        return new UserResponse(
            user.Id,
            user.ExternalRef.Value,
            user.Name,
            user.AccessCode.Value,
            user.Face.ContentHash,
            user.Face.ByteSize,
            user.Face.Width,
            user.Face.Height,
            user.CreatedAt,
            user.UpdatedAt
        );
    }
}

public interface IGetUserService
{
    Task<OneOf<UserResponse, NotFoundError>> ExecuteAsync(
        string? externalRef,
        CancellationToken cancellationToken
    );
}
