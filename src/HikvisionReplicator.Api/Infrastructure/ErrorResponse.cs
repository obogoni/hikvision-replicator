namespace HikvisionReplicator.Api.Infrastructure;

public record ErrorResponse(
    string Type,
    string Message,
    Dictionary<string, string[]>? Errors = null)
{
    public static ErrorResponse NotFound(string message) =>
        new("not_found", message);

    public static ErrorResponse Conflict(string message) =>
        new("conflict", message);

    public static ErrorResponse Validation(Dictionary<string, string[]> errors) =>
        new("validation_error", "One or more fields are invalid.", errors);
}
