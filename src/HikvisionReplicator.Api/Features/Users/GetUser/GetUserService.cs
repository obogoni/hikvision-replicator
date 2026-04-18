using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.GetUser;

public class GetUserService(IRepository<User> repo) : IGetUserService
{
    public async Task<OneOf<UserResponse, NotFoundError>> ExecuteAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var user = await repo.GetByIdAsync(id, cancellationToken);
        if (user is null)
            return new NotFoundError($"User with id '{id}' was not found.");

        return UserResponse.FromEntity(user);
    }
}
