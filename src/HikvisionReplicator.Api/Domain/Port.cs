using CSharpFunctionalExtensions;

namespace HikvisionReplicator.Api.Domain;

public sealed class Port : ValueObject
{
    public int Value { get; }

    private Port(int value) => Value = value;
    private Port() { } // for EF Core

    public static Result<Port, ValidationError> Create(int? value)
    {
        if (value is null)
            return Result.Failure<Port, ValidationError>(new(Errors.Field, Errors.Required));

        if (value < 1 || value > 65535)
            return Result.Failure<Port, ValidationError>(new(Errors.Field, Errors.OutOfRange));

        return Result.Success<Port, ValidationError>(new Port(value.Value));
    }

    internal static Port FromPersistence(int value) => new(value);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static class Errors
    {
        public const string Field = "httpPort";
        public const string Required = "HTTP port is required.";
        public const string OutOfRange = "Must be between 1 and 65535.";
    }
}
