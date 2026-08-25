using HikvisionReplicator.Api.Infrastructure;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// A-13's envelope, and the guards that stop a mis-configured bound reaching the normalizer.
/// The bounds themselves are the device's, taken from Hikvision's DS-K1T606 documentation.
/// </summary>
public class FaceImageOptionsTests
{
    private static ValidateOptionsResult Validate(Action<FaceImageOptions> configure)
    {
        var options = new FaceImageOptions();
        configure(options);
        return new FaceImageOptionsValidator().Validate(null, options);
    }

    private static void Nothing(FaceImageOptions options) { }

    // ─── A-13: the envelope the device will actually accept ──

    [Fact]
    public void Default_byte_band_is_the_forty_to_two_hundred_kilobyte_device_envelope()
    {
        var options = new FaceImageOptions();

        Assert.Equal(40 * 1024, options.MinByteSize);
        Assert.Equal(200 * 1024, options.MaxByteSize);
    }

    [Fact]
    public void Default_edge_bounds_are_the_device_resolution_floor_and_ceiling()
    {
        var options = new FaceImageOptions();

        Assert.Equal(480, options.MinShortEdge);
        Assert.Equal(640, options.MinLongEdge);
        Assert.Equal(2159, options.MaxShortEdge);
        Assert.Equal(3839, options.MaxLongEdge);
    }

    [Fact]
    public void Default_upload_and_decode_caps_are_eight_megabytes_and_forty_megapixels()
    {
        var options = new FaceImageOptions();

        Assert.Equal(8 * 1024 * 1024, options.MaxUploadBytes);
        Assert.Equal(40_000_000, options.MaxDecodePixels);
    }

    [Fact]
    public void Default_quality_ladder_descends_and_the_downscale_factor_shrinks()
    {
        var options = new FaceImageOptions();

        Assert.NotEmpty(options.QualityLadder);
        Assert.Equal(options.QualityLadder.OrderByDescending(q => q), options.QualityLadder);
        Assert.InRange(options.DownscaleFactor, 0.0001, 0.9999);
    }

    [Fact]
    public void Envelope_shipped_by_default_is_accepted()
    {
        Assert.True(Validate(Nothing).Succeeded);
    }

    // ─── A bad bound must abort startup, not the first upload ──

    [Fact]
    public void Byte_band_whose_minimum_is_not_below_its_maximum_is_rejected()
    {
        var result = Validate(o =>
        {
            o.MinByteSize = 200 * 1024;
            o.MaxByteSize = 200 * 1024;
        });

        Assert.Contains(
            FaceImageOptionsValidator.InvertedByteBandMessage,
            result.Failures ?? []
        );
    }

    [Fact]
    public void Byte_band_that_is_not_positive_is_rejected()
    {
        var result = Validate(o => o.MinByteSize = 0);

        Assert.Contains(
            FaceImageOptionsValidator.NonPositiveByteBandMessage,
            result.Failures ?? []
        );
    }

    [Fact]
    public void Edge_range_whose_minimum_is_not_below_its_maximum_is_rejected()
    {
        var result = Validate(o => o.MinShortEdge = 2159);

        Assert.Contains(
            FaceImageOptionsValidator.InvertedEdgeRangeMessage,
            result.Failures ?? []
        );
    }

    [Fact]
    public void Edge_bound_that_is_not_positive_is_rejected()
    {
        var result = Validate(o => o.MinLongEdge = 0);

        Assert.Contains(FaceImageOptionsValidator.NonPositiveEdgeMessage, result.Failures ?? []);
    }

    [Fact]
    public void Short_edge_bound_above_its_long_edge_bound_is_rejected()
    {
        var result = Validate(o => o.MaxShortEdge = 3900);

        Assert.Contains(
            FaceImageOptionsValidator.ShortEdgeExceedsLongEdgeMessage,
            result.Failures ?? []
        );
    }

    [Fact]
    public void Empty_quality_ladder_is_rejected()
    {
        var result = Validate(o => o.QualityLadder.Clear());

        Assert.Contains(
            FaceImageOptionsValidator.EmptyQualityLadderMessage,
            result.Failures ?? []
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public void Quality_outside_one_to_one_hundred_is_rejected(int quality)
    {
        var result = Validate(o =>
        {
            o.QualityLadder.Clear();
            o.QualityLadder.Add(quality);
        });

        Assert.Contains(FaceImageOptionsValidator.QualityOutOfRangeMessage, result.Failures ?? []);
    }

    [Fact]
    public void Quality_ladder_that_does_not_strictly_descend_is_rejected()
    {
        // The ladder is walked top-down and the first quality inside the ceiling wins.
        // Out of order, that rule stops picking the highest acceptable quality.
        var result = Validate(o =>
        {
            o.QualityLadder.Clear();
            o.QualityLadder.Add(70);
            o.QualityLadder.Add(90);
            o.QualityLadder.Add(50);
        });

        Assert.Contains(
            FaceImageOptionsValidator.UnorderedQualityLadderMessage,
            result.Failures ?? []
        );
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(-0.5)]
    public void Downscale_factor_outside_zero_to_one_exclusive_is_rejected(double factor)
    {
        var result = Validate(o => o.DownscaleFactor = factor);

        Assert.Contains(
            FaceImageOptionsValidator.DownscaleFactorOutOfRangeMessage,
            result.Failures ?? []
        );
    }

    [Fact]
    public void Upload_or_decode_cap_that_is_not_positive_is_rejected()
    {
        var result = Validate(o => o.MaxDecodePixels = 0);

        Assert.Contains(
            FaceImageOptionsValidator.NonPositiveUploadCapMessage,
            result.Failures ?? []
        );
    }
}
