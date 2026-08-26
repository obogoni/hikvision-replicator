using CSharpFunctionalExtensions;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public sealed class ExternalRef : ValueObject
{
    public const int MaxLength = 255;

    public string Value { get; }

    private ExternalRef(string value) => Value = value;

    private ExternalRef() => Value = string.Empty; // for EF Core

    public static OneOf<ExternalRef, ValidationError> Create(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return new ValidationError(Errors.Field, Errors.Required);

        if (value.Length > MaxLength)
            return new ValidationError(Errors.Field, Errors.TooLong);

        // Stored byte-exactly: it is an opaque integrator key, so no trimming and no
        // case folding — folding could silently merge two distinct spectators (A-15).
        return new ExternalRef(value);
    }

    internal static ExternalRef FromPersistence(string value) => new(value);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static class Errors
    {
        public const string Field = "externalRef";
        public const string Required = "External reference is required.";
        public const string TooLong = "External reference must be 255 characters or fewer.";
    }
}
