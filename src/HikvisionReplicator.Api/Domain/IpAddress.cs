using CSharpFunctionalExtensions;

namespace HikvisionReplicator.Api.Domain;

public sealed class IpAddress : ValueObject
{
    public string Value { get; }

    private IpAddress(string value) => Value = value;
    private IpAddress() => Value = string.Empty; // for EF Core

    public static Result<IpAddress, ValidationError> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Result.Failure<IpAddress, ValidationError>(new(Errors.Field, Errors.Required));

        if (!System.Net.IPAddress.TryParse(value, out _))
            return Result.Failure<IpAddress, ValidationError>(new(Errors.Field, Errors.InvalidFormat));

        return Result.Success<IpAddress, ValidationError>(new IpAddress(value));
    }

    internal static IpAddress FromPersistence(string value) => new(value);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static class Errors
    {
        public const string Field = "ipAddress";
        public const string Required = "IP address is required.";
        public const string InvalidFormat = "IP address format is invalid.";
    }
}
