using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

public record UpsertUserRequest(string? ExternalRef, string? Name, string? AccessCode, byte[]? FacePic);

public record UserResponse(
    int Id,
    string ExternalRef,
    string Name,
    string AccessCode,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static UserResponse FromEntity(User u) =>
        new(u.Id, u.ExternalRef, u.Name, u.AccessCode.Value, u.CreatedAt, u.UpdatedAt);
}

public record UpsertUserResult(UserResponse User, bool WasCreated);

public interface IUpsertUserService
{
    Task<OneOf<UpsertUserResult, ValidationError>> ExecuteAsync(
        UpsertUserRequest request,
        CancellationToken cancellationToken
    );
}
