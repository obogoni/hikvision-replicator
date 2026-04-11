using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

public static class DomainErrorExtensions
{
    public static IResult ToMinimalApiResult(this ValidationError validationError) =>
        Results.ValidationProblem(
            new Dictionary<string, string[]> { [validationError.Field] = [validationError.Message] });

    public static IResult ToMinimalApiResult(this NotFoundError notFoundError) =>
        Results.Problem(detail: notFoundError.Message, statusCode: StatusCodes.Status404NotFound);

    public static IResult ToMinimalApiResult(this ConflictError conflictError) =>
        Results.Problem(detail: conflictError.Message, statusCode: StatusCodes.Status409Conflict);
}
