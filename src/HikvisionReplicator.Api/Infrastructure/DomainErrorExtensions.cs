using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

public static class DomainErrorExtensions
{
    public static IResult ToMinimalApiResult(this ValidationError validationError) =>
        Results.BadRequest(ErrorResponse.Validation(
            new Dictionary<string, string[]> { [validationError.Field] = [validationError.Message] }));

    public static IResult ToMinimalApiResult(this NotFoundError notFoundError) =>
        Results.NotFound(ErrorResponse.NotFound(notFoundError.Message));

    public static IResult ToMinimalApiResult(this ConflictError conflictError) =>
        Results.Conflict(ErrorResponse.Conflict(conflictError.Message));
}
