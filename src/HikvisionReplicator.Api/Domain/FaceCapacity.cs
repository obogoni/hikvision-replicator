using CSharpFunctionalExtensions;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public sealed class FaceCapacity : ValueObject
{
    public const int Minimum = 1;
    public const int Maximum = 1_000_000;

    public int Value { get; }

    private FaceCapacity(int value) => Value = value;

    private FaceCapacity() { } // for EF Core

    public static OneOf<FaceCapacity, ValidationError> Create(int? value)
    {
        if (value is null)
            return new ValidationError(Errors.Field, Errors.Required);

        if (value < Minimum || value > Maximum)
            return new ValidationError(Errors.Field, Errors.OutOfRange);

        return new FaceCapacity(value.Value);
    }

    internal static FaceCapacity FromPersistence(int value) => new(value);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return Value;
    }

    public static class Errors
    {
        public const string Field = "faceCapacity";
        public const string Required = "Face capacity is required.";
        public const string OutOfRange = "Must be between 1 and 1000000.";
    }
}
