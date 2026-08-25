using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

/// <summary>
/// The full representation of a spectator (A-2). The external reference is not part of the body:
/// the integrator owns the key and names it in the URL.
/// </summary>
/// <param name="FacePicture">
/// The raw upload, base64-encoded in JSON. Mandatory when the spectator does not yet exist;
/// omitting it on an update keeps the stored image (A-4). It is the sole exception to the
/// full-representation rule.
/// </param>
public record UpsertUserRequest(string? Name, string? AccessCode, byte[]? FacePicture);

/// <summary>
/// What the registry holds for a spectator. It carries the face picture's fingerprint and
/// <b>never its bytes</b> (USR-09): the derivative exists to be pushed to a device, not to be
/// served back, and returning 40-200 KB on every response would defeat the whole point of
/// keeping the bytes out of the catalogue.
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

/// <summary>A spectator that did not exist before this request (201).</summary>
public record UserCreated(UserResponse User);

/// <summary>A spectator that already existed and was rewritten (200).</summary>
public record UserUpdated(UserResponse User);

/// <summary>
/// The idempotent upsert behind <c>PUT /api/users/{externalRef}</c> (A-2).
/// <para>
/// Created and updated are two success arms rather than one flagged result so the endpoint can
/// map them straight to 201 and 200 through <c>Match</c>, with no branch in the transport layer
/// (AD-003).
/// </para>
/// </summary>
public interface IUpsertUserService
{
    Task<OneOf<UserCreated, UserUpdated, ValidationError, ConflictError>> ExecuteAsync(
        string? externalRef,
        UpsertUserRequest request,
        CancellationToken cancellationToken
    );
}
