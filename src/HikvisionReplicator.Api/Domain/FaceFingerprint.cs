using CSharpFunctionalExtensions;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

/// <summary>
/// The denormalized half of A-1: what Phase 2 needs to detect a changed face without
/// reading the stored bytes (USR-22). Constructed only from the normalizer's output,
/// never from caller-supplied input.
/// </summary>
public sealed class FaceFingerprint : ValueObject
{
    public string ContentHash { get; }
    public int ByteSize { get; }
    public int Width { get; }
    public int Height { get; }

    private FaceFingerprint(string contentHash, int byteSize, int width, int height)
    {
        ContentHash = contentHash;
        ByteSize = byteSize;
        Width = width;
        Height = height;
    }

    private FaceFingerprint() => ContentHash = string.Empty; // for EF Core

    public static OneOf<FaceFingerprint, ValidationError> Create(
        string? contentHash,
        int byteSize,
        int width,
        int height
    )
    {
        if (string.IsNullOrWhiteSpace(contentHash))
            return new ValidationError(Errors.Field, Errors.HashRequired);

        if (byteSize <= 0)
            return new ValidationError(Errors.Field, Errors.ByteSizeNotPositive);

        if (width <= 0 || height <= 0)
            return new ValidationError(Errors.Field, Errors.DimensionsNotPositive);

        return new FaceFingerprint(contentHash, byteSize, width, height);
    }

    internal static FaceFingerprint FromPersistence(
        string contentHash,
        int byteSize,
        int width,
        int height
    ) => new(contentHash, byteSize, width, height);

    protected override IEnumerable<object> GetEqualityComponents()
    {
        yield return ContentHash;
        yield return ByteSize;
        yield return Width;
        yield return Height;
    }

    public static class Errors
    {
        public const string Field = "facePicture";
        public const string HashRequired = "Face picture content hash is required.";
        public const string ByteSizeNotPositive = "Face picture byte size must be positive.";
        public const string DimensionsNotPositive = "Face picture dimensions must be positive.";
    }
}
