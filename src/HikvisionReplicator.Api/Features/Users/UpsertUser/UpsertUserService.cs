using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

public class UpsertUserService(IRepository<User> repo) : IUpsertUserService
{
    public async Task<OneOf<UpsertUserResult, ValidationError>> ExecuteAsync(
        UpsertUserRequest req,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(req.ExternalRef))
            return new ValidationError(User.Errors.ExternalRefField, User.Errors.ExternalRefRequired);
        if (req.ExternalRef.Length > 255)
            return new ValidationError(User.Errors.ExternalRefField, User.Errors.ExternalRefTooLong);

        var existing = await repo.FirstOrDefaultAsync(
            new UserByExternalRefSpec(req.ExternalRef),
            cancellationToken
        );

        if (existing is not null)
        {
            var updateResult = existing.Update(req.Name, req.AccessCode, req.FacePic);
            if (updateResult.TryPickT1(out var validationError, out _))
                return validationError;

            await repo.UpdateAsync(existing, cancellationToken);
            return new UpsertUserResult(UserResponse.FromEntity(existing), WasCreated: false);
        }

        var createResult = User.Create(req.Name, req.AccessCode, req.FacePic, req.ExternalRef, DateTime.UtcNow);
        if (createResult.TryPickT1(out var createError, out var user))
            return createError;

        await repo.AddAsync(user, cancellationToken);
        return new UpsertUserResult(UserResponse.FromEntity(user), WasCreated: true);
    }
}
