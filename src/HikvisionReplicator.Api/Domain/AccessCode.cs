using CSharpFunctionalExtensions;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public sealed class AccessCode : ValueObject
{
    public const int MinLength = 4;
    public const int MaxLength = 20;

    public string Value { get; }

    private AccessCode(string value) => Value = value;

    private AccessCode() => Value = string.Empty; // for EF Core

    public static OneOf<AccessCode, ValidationError> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ValidationError(Errors.Field, Errors.Required);

        // ASCII '0'-'9' only, deliberately not char.IsDigit: that accepts Arabic-Indic
        // and other Unicode digits, which no device keypad can produce (A-10).
        foreach (var character in value)
        {
            if (character is < '0' or > '9')
                return new ValidationError(Errors.Field, Errors.MustBeNumeric);
        }

        if (value.Length < MinLength || value.Length > MaxLength)
            return new ValidationError(Errors.Field, Errors.OutOfRange);

        return new AccessCode(value);
    }

    internal static AccessCode FromPersistence(string value) => new(value);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static class Errors
    {
        public const string Field = "accessCode";
        public const string Required = "Access code is required.";
        public const string MustBeNumeric = "Access code must contain only the digits 0-9.";
        public const string OutOfRange = "Access code must be between 4 and 20 digits.";
    }
}
