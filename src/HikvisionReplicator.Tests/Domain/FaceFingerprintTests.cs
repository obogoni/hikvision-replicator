using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class FaceFingerprintTests
{
    private const string Hash = "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08";

    // ─── USR-22: the hash, byte size and dimensions are recorded on the user ───

    [Fact]
    public void Fingerprint_carries_the_hash_size_and_dimensions_of_the_stored_picture()
    {
        var result = FaceFingerprint.Create(Hash, 81_920, 720, 960);

        Assert.True(result.IsT0);
        Assert.Equal(Hash, result.AsT0.ContentHash);
        Assert.Equal(81_920, result.AsT0.ByteSize);
        Assert.Equal(720, result.AsT0.Width);
        Assert.Equal(960, result.AsT0.Height);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Fingerprint_without_a_content_hash_is_rejected(string? contentHash)
    {
        var result = FaceFingerprint.Create(contentHash, 81_920, 720, 960);

        Assert.True(result.IsT1);
        Assert.Equal("facePicture", result.AsT1.Field);
        Assert.Equal(FaceFingerprint.Errors.HashRequired, result.AsT1.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Fingerprint_without_a_positive_byte_size_is_rejected(int byteSize)
    {
        var result = FaceFingerprint.Create(Hash, byteSize, 720, 960);

        Assert.True(result.IsT1);
        Assert.Equal("facePicture", result.AsT1.Field);
        Assert.Equal(FaceFingerprint.Errors.ByteSizeNotPositive, result.AsT1.Message);
    }

    [Theory]
    [InlineData(0, 960)]
    [InlineData(720, 0)]
    [InlineData(-720, 960)]
    [InlineData(720, -960)]
    public void Fingerprint_without_positive_dimensions_is_rejected(int width, int height)
    {
        var result = FaceFingerprint.Create(Hash, 81_920, width, height);

        Assert.True(result.IsT1);
        Assert.Equal("facePicture", result.AsT1.Field);
        Assert.Equal(FaceFingerprint.Errors.DimensionsNotPositive, result.AsT1.Message);
    }

    // ─── Equality is by all four components: Phase 2 detects a changed face by it ───

    [Fact]
    public void Two_fingerprints_of_the_same_picture_are_equal()
    {
        var first = FaceFingerprint.Create(Hash, 81_920, 720, 960).AsT0;
        var second = FaceFingerprint.Create(Hash, 81_920, 720, 960).AsT0;

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000", 81_920, 720, 960)]
    [InlineData(Hash, 81_921, 720, 960)]
    [InlineData(Hash, 81_920, 721, 960)]
    [InlineData(Hash, 81_920, 720, 961)]
    public void Fingerprints_differing_in_any_component_are_not_equal(
        string contentHash,
        int byteSize,
        int width,
        int height
    )
    {
        var stored = FaceFingerprint.Create(Hash, 81_920, 720, 960).AsT0;
        var candidate = FaceFingerprint.Create(contentHash, byteSize, width, height).AsT0;

        Assert.NotEqual(stored, candidate);
    }

    // ─── AD-009: rehydration from the database bypasses validation ───

    [Fact]
    public void Fingerprint_rehydrated_from_storage_keeps_values_creation_would_reject()
    {
        var rehydrated = FaceFingerprint.FromPersistence("", 0, 0, 0);

        Assert.Equal(string.Empty, rehydrated.ContentHash);
        Assert.Equal(0, rehydrated.ByteSize);
        Assert.True(FaceFingerprint.Create("", 0, 0, 0).IsT1);
    }
}
