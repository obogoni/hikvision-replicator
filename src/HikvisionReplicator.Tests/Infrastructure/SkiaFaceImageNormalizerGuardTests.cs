using System.Diagnostics;
using HikvisionReplicator.Api.Infrastructure;
using SkiaSharp;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// Everything the normalizer refuses, and the order it refuses in.
/// <para>
/// Several of these assert on <em>which</em> rejection came back rather than merely that one did.
/// That is deliberate and it is the only deterministic way to prove ordering: the oversized
/// fixture is also not a decodable image, and the decode-bomb fixture's pixel data is unusable,
/// so an implementation that decoded first would come back with a different message. Asserting
/// the exact message is therefore an assertion that the cheap guard ran before the expensive one.
/// </para>
/// </summary>
public class SkiaFaceImageNormalizerGuardTests
{
    // ─── USR-19: refuse an oversized upload without constructing a codec ──

    [Fact]
    public void Upload_over_the_size_cap_is_refused_before_a_codec_is_constructed()
    {
        var oversized = new byte[9 * 1024 * 1024];

        var result = FaceImageNormalizerFactory.Build().Normalize(oversized);

        // These bytes are also not a decodable image. Getting the size message back — and not
        // the not-decodable one — is what proves nothing tried to read them.
        var error = result.AsT1;
        Assert.Equal(SkiaFaceImageNormalizer.Errors.Field, error.Field);
        Assert.Equal(SkiaFaceImageNormalizer.Errors.UploadTooLarge, error.Message);
    }

    [Fact]
    public void Upload_exactly_at_the_size_cap_is_not_refused_for_size()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.Grayscale);

        var result = FaceImageNormalizerFactory
            .Build(o => o.MaxUploadBytes = upload.Length)
            .Normalize(upload);

        Assert.True(result.IsT0);
    }

    // ─── USR-21: bytes that are not an image ──

    [Fact]
    public void Bytes_that_are_not_an_image_are_refused_naming_the_face_picture_field()
    {
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.NotAnImage));

        var error = result.AsT1;
        Assert.Equal("facePicture", error.Field);
        Assert.Equal(SkiaFaceImageNormalizer.Errors.NotDecodable, error.Message);
    }

    [Fact]
    public void Empty_upload_is_refused_as_not_a_decodable_image()
    {
        var result = FaceImageNormalizerFactory.Build().Normalize([]);

        var error = result.AsT1;
        Assert.Equal(SkiaFaceImageNormalizer.Errors.Field, error.Field);
        Assert.Equal(SkiaFaceImageNormalizer.Errors.NotDecodable, error.Message);
    }

    // ─── USR-20: refuse before a decode buffer exists ──

    [Fact]
    public void Image_declaring_more_pixels_than_the_cap_is_refused_before_a_buffer_is_allocated()
    {
        var bomb = FaceFixtures.Bytes(FaceFixtures.DecodeBomb);
        Assert.True(bomb.Length < 1024, "the decode bomb must stay a tiny file to mean anything");

        var before = CurrentPrivateMemory();
        var result = FaceImageNormalizerFactory.Build().Normalize(bomb);
        var growth = CurrentPrivateMemory() - before;

        var error = result.AsT1;
        Assert.Equal(SkiaFaceImageNormalizer.Errors.Field, error.Field);

        // Two independent sensors on the same criterion. First: the fixture's pixel data cannot
        // be decoded, so an implementation that allocated and decoded first would have come back
        // with the not-decodable message instead of this one.
        Assert.Equal(SkiaFaceImageNormalizer.Errors.TooManyPixels, error.Message);

        // Second: the fixture declares 30000x30000, which is 3.6 GB of decode buffer. Refusing
        // it must not cost anything close to that.
        Assert.True(
            growth < 256L * 1024 * 1024,
            $"refusing a 900-megapixel header grew private memory by {growth} bytes, "
                + "which means a decode buffer was allocated before the cap was checked"
        );
    }

    [Fact]
    public void Image_declaring_exactly_the_pixel_cap_is_not_refused_for_size()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.Grayscale);

        var result = FaceImageNormalizerFactory
            .Build(o => o.MaxDecodePixels = 1200 * 900)
            .Normalize(upload);

        Assert.True(result.IsT0);
    }

    // ─── USR-17: the resolution floor, and never upscaling to satisfy it ──

    [Fact]
    public void Image_below_the_resolution_floor_is_refused_with_a_message_stating_the_minimum()
    {
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.SubFloorThumbnail));

        var error = result.AsT1;
        Assert.Equal(SkiaFaceImageNormalizer.Errors.Field, error.Field);
        Assert.Contains("480", error.Message, StringComparison.Ordinal);
        Assert.Contains("640", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Image_below_the_resolution_floor_is_never_upscaled_into_compliance()
    {
        // 320x240. Upscaling it would manufacture a file that satisfies the envelope and cannot
        // be recognised, so nothing may come back at all.
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.SubFloorThumbnail));

        Assert.False(result.IsT0);
    }

    // ─── The floor is judged on oriented dimensions, not encoded ones ──

    [Fact]
    public void Portrait_photograph_stored_with_a_rotated_orientation_tag_clears_the_floor()
    {
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.ExifRotatedPortrait));

        Assert.True(result.IsT0);
    }

    [Fact]
    public void Portrait_photograph_stored_with_a_rotated_orientation_tag_is_measured_upright()
    {
        // Stored 1200x900 with Orientation=6. The image is a 900x1200 portrait; an implementation
        // reading codec.Info would be measuring a landscape image that does not exist.
        var normalized = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.ExifRotatedPortrait))
            .AsT0;

        Assert.True(
            normalized.Width < normalized.Height,
            $"expected a portrait derivative, got {normalized.Width}x{normalized.Height}"
        );
    }

    [Theory]
    [InlineData(SKEncodedOrigin.TopLeft, 1200, 900)]
    [InlineData(SKEncodedOrigin.TopRight, 1200, 900)]
    [InlineData(SKEncodedOrigin.BottomRight, 1200, 900)]
    [InlineData(SKEncodedOrigin.BottomLeft, 1200, 900)]
    [InlineData(SKEncodedOrigin.LeftTop, 900, 1200)]
    [InlineData(SKEncodedOrigin.RightTop, 900, 1200)]
    [InlineData(SKEncodedOrigin.RightBottom, 900, 1200)]
    [InlineData(SKEncodedOrigin.LeftBottom, 900, 1200)]
    public void Quarter_turn_orientations_swap_the_dimensions_and_the_others_leave_them_alone(
        SKEncodedOrigin origin,
        int expectedWidth,
        int expectedHeight
    )
    {
        // Every origin is listed because SkiaSharp has no auto-orient: a case falling through to
        // a default is a silently mis-measured photograph, not a compile error.
        var (width, height) = SkiaFaceImageNormalizer.Orient(1200, 900, origin);

        Assert.Equal(expectedWidth, width);
        Assert.Equal(expectedHeight, height);
    }

    [Fact]
    public void Photograph_inside_the_envelope_is_accepted()
    {
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.Grayscale));

        Assert.True(result.IsT0);
    }

    private static long CurrentPrivateMemory()
    {
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        return process.PrivateMemorySize64;
    }
}
