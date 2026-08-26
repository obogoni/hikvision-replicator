using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Features.Users.ListUsers;

public class ListUsersService(IUserRepository repository) : IListUsersService
{
    public const int DefaultPageSize = 50;

    /// <summary>
    /// The largest page anyone gets, whatever they ask for (USR-43). A caller asking for
    /// everything at once is the bare-array shape by another name.
    /// </summary>
    public const int MaxPageSize = 200;

    public async Task<UserPageResponse> ExecuteAsync(
        int? page,
        int? pageSize,
        CancellationToken cancellationToken
    )
    {
        // Clamped rather than refused, in both directions: a nonsensical page request is
        // answered with the nearest sensible page, never with an error (USR-43, USR-45).
        var currentPage = Math.Max(page ?? 1, 1);
        var currentSize = Math.Clamp(pageSize ?? DefaultPageSize, 1, MaxPageSize);

        // Widened to long first: (page - 1) * size overflows int for a large enough page number,
        // and a negative skip is a 500 an unauthenticated caller could ask for at will (A-11).
        var skip = (int)Math.Min((long)(currentPage - 1) * currentSize, int.MaxValue);

        // One row past the page. That single row answers "is there another page?" without a
        // second count query across 50,000 spectators, and it is never returned.
        var window = await repository.ListAsync(
            new ActiveUsersPagedSpec(skip, currentSize + 1),
            cancellationToken
        );

        return new UserPageResponse(
            [.. window.Take(currentSize).Select(UserResponse.FromEntity)],
            currentPage,
            currentSize,
            window.Count > currentSize
        );
    }
}
