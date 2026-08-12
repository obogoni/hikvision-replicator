using CSharpFunctionalExtensions;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public sealed class IpAddress : ValueObject
{
    public string Value { get; }

    private IpAddress(string value) => Value = value;

    private IpAddress() => Value = string.Empty; // for EF Core

    public static OneOf<IpAddress, ValidationError> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ValidationError(Errors.Field, Errors.Required);

        if (!System.Net.IPAddress.TryParse(value, out var parsed))
            return new ValidationError(Errors.Field, Errors.InvalidFormat);

        // Store the normalized form so that non-canonical spellings of one address
        // (192.168.001.001 and 192.168.1.1) share a single unique-index key.
        return new IpAddress(parsed.ToString());
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
