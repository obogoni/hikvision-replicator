using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Api.Features.Users.ListUsers;

/// <summary>
/// One spectator as the catalogue reports them. Carries the face picture's fingerprint and
/// <b>never its bytes</b> (USR-37, A-1): this is the path where that matters most — 40-200 KB on
/// every row of every page is precisely the bloat OD-4 exists to prevent.
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

/// <summary>
/// One page of the catalogue (USR-42).
/// <para>
/// <b>Paged from the first commit, unlike <c>ListDevices</c>.</b> That slice returns a bare
/// array, which is recorded as known gap DEV-26; at the 50,000 spectators this registry is sized
/// for, the same shape would put ~50 MB of JSON on one response. The gap is not repeated here.
/// </para>
/// </summary>
/// <param name="Page">The 1-based page this response answers, after clamping.</param>
/// <param name="PageSize">The page size actually used, which may be smaller than the one asked
/// for (USR-43).</param>
/// <param name="HasMore">
/// Whether another page follows. Together with <paramref name="Page"/> it is everything a caller
/// needs to ask for the next one, and it costs one extra row rather than a second count query
/// over the whole catalogue.
/// </param>
public record UserPageResponse(
    IReadOnlyList<UserResponse> Items,
    int Page,
    int PageSize,
    bool HasMore
);

/// <summary>
/// Listing cannot fail, so it returns the value directly rather than a <c>OneOf</c> (AD-003).
/// An empty registry is an empty page, never an error (USR-45).
/// </summary>
public interface IListUsersService
{
    Task<UserPageResponse> ExecuteAsync(int? page, int? pageSize, CancellationToken cancellationToken);
}
