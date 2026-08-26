using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using SkiaSharp;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// What comes out of the normalizer: a canonical JPEG, the right way up, in one colour space,
/// inside the device's resolution ceiling, with the source's proportions intact.
/// </summary>
public class SkiaFaceImageNormalizerImageTests
{
    private static NormalizedFaceImage Normalize(string fixture) =>
        FaceImageNormalizerFactory.Build().Normalize(FaceFixtures.Bytes(fixture)).AsT0;

    // ─── USR-12: one canonical format, and never the original ──

    [Fact]
    public void Photograph_supplied_as_a_png_is_stored_as_a_jpeg()
    {
        var derivative = Normalize(FaceFixtures.Png).Content;

        var (marker, _, _, _) = JpegInspector.Frame(derivative);
        Assert.Equal(FixtureHeader.Jpeg, FixtureHeader.Read(derivative).Format);
        Assert.True(JpegInspector.IsStartOfFrame(marker));
    }

    [Fact]
    public void Photograph_supplied_as_a_png_is_not_stored_as_it_arrived()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.Png);

        var derivative = Normalize(FaceFixtures.Png).Content;

        Assert.NotEqual(upload, derivative);
    }

    [Fact]
    public void Photograph_supplied_as_a_progressive_jpeg_is_stored_as_a_baseline_jpeg()
    {
        var derivative = Normalize(FaceFixtures.Progressive).Content;

        var (marker, _, _, _) = JpegInspector.Frame(derivative);
        Assert.Equal(JpegInspector.BaselineStartOfFrame, marker);
    }

    // ─── USR-13: the stored face is upright ──

    [Fact]
    public void Photograph_stored_sideways_with_an_orientation_tag_is_stored_upright()
    {
        // Encoded 1200x900 with an orientation tag saying it must be turned. The derivative is
        // the 900x1200 portrait a viewer would see, not the landscape the pixels were stored as.
        var normalized = Normalize(FaceFixtures.ExifRotatedPortrait);

        Assert.Equal(900, normalized.Width);
        Assert.Equal(1200, normalized.Height);
    }

    [Fact]
    public void Photograph_stored_sideways_is_turned_the_way_its_orientation_tag_asks()
    {
        // Dimensions alone cannot tell a quarter turn clockwise from a quarter turn the other
        // way -- both give 900x1200. EXIF orientation 6 means the stored image must be rotated
        // 90 degrees clockwise to be seen correctly, which sends the stored top-left corner to
        // the top-right of the display image, and the stored bottom-left corner to the top-left.
        // That is what is checked here, on corner averages so JPEG artefacts cannot matter.
        using var stored = DecodeIgnoringOrientation(FaceFixtures.ExifRotatedPortrait);
        using var derivative = SKBitmap.Decode(Normalize(FaceFixtures.ExifRotatedPortrait).Content);

        AssertCornersMatch(CornerAverage(stored, Corner.TopLeft), CornerAverage(derivative, Corner.TopRight));
        AssertCornersMatch(CornerAverage(stored, Corner.TopRight), CornerAverage(derivative, Corner.BottomRight));
        AssertCornersMatch(CornerAverage(stored, Corner.BottomRight), CornerAverage(derivative, Corner.BottomLeft));
        AssertCornersMatch(CornerAverage(stored, Corner.BottomLeft), CornerAverage(derivative, Corner.TopLeft));
    }

    // ─── The derivative is sRGB, whatever the source claimed ──

    [Fact]
    public void Grayscale_photograph_is_stored_as_a_three_channel_colour_image()
    {
        // A single-component JPEG is a colour space of its own as far as a device is concerned.
        var (_, _, _, components) = JpegInspector.Frame(Normalize(FaceFixtures.Grayscale).Content);

        Assert.Equal(3, components);
    }

    [Fact]
    public void Grayscale_photograph_normalizes()
    {
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.Grayscale));

        Assert.True(result.IsT0);
    }

    [Fact]
    public void Four_channel_print_photograph_is_stored_as_a_three_channel_colour_image()
    {
        // CMYK is the third colour space the spec's edge case names, and the one that had no
        // fixture at all until the Verifier noticed (L-036). It is also the worst to get wrong:
        // a four-component JPEG handed to a device that assumes three does not fail loudly, it
        // renders inverted — a plausible-looking face that will never match anyone.
        var (_, _, _, components) = JpegInspector.Frame(Normalize(FaceFixtures.Cmyk).Content);

        Assert.Equal(3, components);
    }

    [Fact]
    public void Photograph_carrying_a_colour_profile_does_not_carry_it_forward()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.IccProfiled);
        var sourceProfile = ColourProfile(upload);
        Assert.NotEmpty(sourceProfile);

        var derivative = Normalize(FaceFixtures.IccProfiled).Content;

        // The fixture's profile sits at a 5000 K white point. If it survived the round trip the
        // conversion never happened.
        Assert.NotEqual(sourceProfile, ColourProfile(derivative));
    }

    [Fact]
    public void Every_photograph_leaves_in_the_same_colour_space_whatever_it_arrived_in()
    {
        // A grayscale source, a 5000 K-profiled source and a plain PNG are three colour spaces
        // going in. The device must be handed one coming out, and this is the assertion that
        // says so: not "some profile", but the same profile every time.
        var profiles = FaceFixtures
            .Photographic.Select(fixture => ColourProfile(Normalize(fixture).Content))
            .ToList();

        Assert.NotEmpty(profiles[0]);
        Assert.All(profiles, profile => Assert.Equal(profiles[0], profile));
    }

    // ─── USR-16: the resolution ceiling ──

    [Fact]
    public void Photograph_above_the_ceiling_is_brought_inside_it()
    {
        var options = new FaceImageOptions();

        var normalized = Normalize(FaceFixtures.LargeFractal);

        Assert.True(
            Math.Min(normalized.Width, normalized.Height) <= options.MaxShortEdge,
            $"shorter edge {Math.Min(normalized.Width, normalized.Height)} exceeds the ceiling"
        );
        Assert.True(
            Math.Max(normalized.Width, normalized.Height) <= options.MaxLongEdge,
            $"longer edge {Math.Max(normalized.Width, normalized.Height)} exceeds the ceiling"
        );
    }

    [Fact]
    public void Photograph_already_inside_the_ceiling_keeps_its_dimensions()
    {
        // 1200x900 is inside the envelope on every edge; there is nothing to correct.
        var normalized = Normalize(FaceFixtures.Grayscale);

        Assert.Equal(1200, normalized.Width);
        Assert.Equal(900, normalized.Height);
    }

    // ─── USR-18: proportions preserved, nothing cropped ──

    [Theory]
    [InlineData(FaceFixtures.ExifRotatedPortrait)]
    [InlineData(FaceFixtures.LargeFractal)]
    [InlineData(FaceFixtures.Png)]
    [InlineData(FaceFixtures.Grayscale)]
    [InlineData(FaceFixtures.Progressive)]
    [InlineData(FaceFixtures.IccProfiled)]
    [InlineData(FaceFixtures.GpsTagged)]
    public void Photograph_keeps_the_proportions_it_arrived_with(string fixture)
    {
        var (sourceWidth, sourceHeight) = OrientedSourceDimensions(fixture);
        var normalized = Normalize(fixture);

        var sourceRatio = (double)sourceWidth / sourceHeight;
        var derivativeRatio = (double)normalized.Width / normalized.Height;

        // A crop would move the ratio; integer rounding of the two edges cannot move it by more
        // than one pixel's worth on the shorter edge.
        var tolerance = sourceRatio / Math.Min(normalized.Width, normalized.Height);

        Assert.True(
            Math.Abs(sourceRatio - derivativeRatio) <= tolerance,
            $"{fixture}: {sourceWidth}x{sourceHeight} became {normalized.Width}x{normalized.Height}"
        );
    }

    // ─── helpers ──

    private enum Corner
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
    }

    private const int CornerBlock = 60;

    /// <summary>The stored pixels, with the orientation tag deliberately not applied.</summary>
    private static SKBitmap DecodeIgnoringOrientation(string fixture)
    {
        using var stream = new MemoryStream(FaceFixtures.Bytes(fixture));
        using var codec = SKCodec.Create(stream);
        var info = new SKImageInfo(
            codec.Info.Width,
            codec.Info.Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul,
            SKColorSpace.CreateSrgb()
        );
        var bitmap = new SKBitmap(info);
        codec.GetPixels(info, bitmap.GetPixels());
        return bitmap;
    }

    private static (int Red, int Green, int Blue) CornerAverage(SKBitmap bitmap, Corner corner)
    {
        var left = corner is Corner.TopRight or Corner.BottomRight ? bitmap.Width - CornerBlock : 0;
        var top = corner is Corner.BottomLeft or Corner.BottomRight ? bitmap.Height - CornerBlock : 0;

        long red = 0,
            green = 0,
            blue = 0;
        for (var y = top; y < top + CornerBlock; y++)
        {
            for (var x = left; x < left + CornerBlock; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                red += pixel.Red;
                green += pixel.Green;
                blue += pixel.Blue;
            }
        }

        var pixels = CornerBlock * CornerBlock;
        return ((int)(red / pixels), (int)(green / pixels), (int)(blue / pixels));
    }

    private static void AssertCornersMatch(
        (int Red, int Green, int Blue) expected,
        (int Red, int Green, int Blue) actual
    )
    {
        const int tolerance = 8;
        Assert.True(
            Math.Abs(expected.Red - actual.Red) <= tolerance
                && Math.Abs(expected.Green - actual.Green) <= tolerance
                && Math.Abs(expected.Blue - actual.Blue) <= tolerance,
            $"expected a corner near {expected}, found {actual}"
        );
    }

    /// <summary>The embedded ICC profile's bytes, or empty when the file carries none.</summary>
    private static byte[] ColourProfile(byte[] jpeg) =>
        JpegInspector
            .Segments(jpeg)
            .Where(segment => segment.Marker == JpegInspector.App2)
            .Select(segment => segment.Payload.ToArray())
            .FirstOrDefault([]);

    private static (int Width, int Height) OrientedSourceDimensions(string fixture)
    {
        using var stream = new MemoryStream(FaceFixtures.Bytes(fixture));
        using var codec = SKCodec.Create(stream);
        return SkiaFaceImageNormalizer.Orient(
            codec.Info.Width,
            codec.Info.Height,
            codec.EncodedOrigin
        );
    }
}
