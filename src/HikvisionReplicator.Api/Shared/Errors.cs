namespace HikvisionReplicator.Api.Shared;

public record ValidationError(string Field, string Message);
public record NotFoundError(string Message);
public record ConflictError(string Message);

public readonly record struct Success;
