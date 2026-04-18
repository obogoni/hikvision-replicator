using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.GetUser;

public record UserResponse(
    int Id,
    string Name,
    string AccessCode,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static UserResponse FromEntity(User u) =>
        new(u.Id, u.Name, u.AccessCode.Value, u.CreatedAt, u.UpdatedAt);
}

public interface IGetUserService
{
    Task<OneOf<UserResponse, NotFoundError>> ExecuteAsync(
        int id,
        CancellationToken cancellationToken
    );
}
