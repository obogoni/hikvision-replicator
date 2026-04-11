using CSharpFunctionalExtensions;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public sealed class AccessCode : ValueObject
{
    public string Value { get; }

    private AccessCode(string value) => Value = value;

    private AccessCode() => Value = string.Empty; // for EF Core

    public static OneOf<AccessCode, ValidationError> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ValidationError(Errors.Field, Errors.Required);

        if (!value.All(char.IsDigit))
            return new ValidationError(Errors.Field, Errors.MustBeNumeric);

        if (value.Length < 4 || value.Length > 20)
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
        public const string MustBeNumeric = "Access code must contain only digits.";
        public const string OutOfRange = "Access code must be between 4 and 20 digits.";
    }
}
