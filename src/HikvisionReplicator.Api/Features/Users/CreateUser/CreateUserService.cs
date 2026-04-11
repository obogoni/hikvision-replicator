using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.CreateUser;

public class CreateUserService(IRepository<User> repo) : ICreateUserService
{
    public async Task<OneOf<UserResponse, ValidationError>> ExecuteAsync(
        CreateUserRequest req,
        CancellationToken cancellationToken
    )
    {
        return await User.Create(req.Name, req.AccessCode, req.FacePic, DateTime.UtcNow)
            .Match<Task<OneOf<UserResponse, ValidationError>>>(
                async user =>
                {
                    await repo.AddAsync(user, cancellationToken);
                    return UserResponse.FromEntity(user);
                },
                validationError =>
                    Task.FromResult<OneOf<UserResponse, ValidationError>>(validationError)
            );
    }
}
