using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The device's accepted output envelope (A-13) as configuration rather than constants.
/// <para>
/// A-13 carries a Phase 3 verification obligation: the figures come from Hikvision's official
/// DS-K1T606 documentation and must be re-checked by <c>isapi-device-client</c> against real
/// hardware. Holding them here makes that correction a configuration change, not a code change.
/// </para>
/// </summary>
public sealed class FaceImageOptions
{
    public const string SectionName = "FaceImage";

    /// <summary>USR-19: uploads larger than this are refused without constructing a codec.</summary>
    public int MaxUploadBytes { get; set; } = 8 * 1024 * 1024;

    /// <summary>
    /// USR-20: the largest declared pixel count we are willing to allocate a decode buffer for.
    /// Checked against the header, so a small compressed file that expands to gigabytes is
    /// refused before the allocation rather than during it.
    /// </summary>
    public long MaxDecodePixels { get; set; } = 40_000_000;

    /// <summary>
    /// USR-15 lower bound. Not a typo and not a formality: over-compression is a device
    /// rejection cause, and a photograph that cannot reach 40 KB is nearly uniform.
    /// </summary>
    public int MinByteSize { get; set; } = 40 * 1024;

    /// <summary>USR-15 upper bound.</summary>
    public int MaxByteSize { get; set; } = 200 * 1024;

    public int MinShortEdge { get; set; } = 480;
    public int MinLongEdge { get; set; } = 640;
    public int MaxShortEdge { get; set; } = 2159;
    public int MaxLongEdge { get; set; } = 3839;

    /// <summary>
    /// Walked strictly in the order given, highest first. A fixed ladder rather than a
    /// bisection search because USR-26 needs identical input to produce identical output bytes:
    /// a search converges to different quality values from different starting conditions.
    /// </summary>
    public IList<int> QualityLadder { get; } = [95, 90, 85, 80, 75, 70, 65, 60, 55, 50];

    /// <summary>
    /// How much each edge shrinks when the whole ladder is still over <see cref="MaxByteSize"/>.
    /// </summary>
    public double DownscaleFactor { get; set; } = 0.8;
}

/// <summary>
/// Validates the envelope. Wired with ValidateOnStart() so a bound that cannot be satisfied
/// aborts startup instead of failing on the first upload — by which point a spectator is already
/// at the turnstile.
/// </summary>
public sealed class FaceImageOptionsValidator : IValidateOptions<FaceImageOptions>
{
    public const string InvertedByteBandMessage =
        $"{FaceImageOptions.SectionName}:MinByteSize must be less than MaxByteSize.";

    public const string NonPositiveByteBandMessage =
        $"{FaceImageOptions.SectionName}:MinByteSize and MaxByteSize must be positive.";

    public const string InvertedEdgeRangeMessage =
        $"{FaceImageOptions.SectionName}: every minimum edge must be less than its maximum.";

    public const string NonPositiveEdgeMessage =
        $"{FaceImageOptions.SectionName}: every edge bound must be positive.";

    public const string ShortEdgeExceedsLongEdgeMessage =
        $"{FaceImageOptions.SectionName}:MinShortEdge must not exceed MinLongEdge, and "
        + "MaxShortEdge must not exceed MaxLongEdge.";

    public const string EmptyQualityLadderMessage =
        $"{FaceImageOptions.SectionName}:QualityLadder must contain at least one quality.";

    public const string QualityOutOfRangeMessage =
        $"{FaceImageOptions.SectionName}:QualityLadder values must be between 1 and 100.";

    public const string UnorderedQualityLadderMessage =
        $"{FaceImageOptions.SectionName}:QualityLadder must be in strictly descending order.";

    public const string DownscaleFactorOutOfRangeMessage =
        $"{FaceImageOptions.SectionName}:DownscaleFactor must be greater than 0 and less than 1.";

    public const string NonPositiveUploadCapMessage =
        $"{FaceImageOptions.SectionName}:MaxUploadBytes and MaxDecodePixels must be positive.";

    public ValidateOptionsResult Validate(string? name, FaceImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.MaxUploadBytes <= 0 || options.MaxDecodePixels <= 0)
            return ValidateOptionsResult.Fail(NonPositiveUploadCapMessage);

        if (options.MinByteSize <= 0 || options.MaxByteSize <= 0)
            return ValidateOptionsResult.Fail(NonPositiveByteBandMessage);

        if (options.MinByteSize >= options.MaxByteSize)
            return ValidateOptionsResult.Fail(InvertedByteBandMessage);

        if (
            options.MinShortEdge <= 0
            || options.MinLongEdge <= 0
            || options.MaxShortEdge <= 0
            || options.MaxLongEdge <= 0
        )
            return ValidateOptionsResult.Fail(NonPositiveEdgeMessage);

        if (
            options.MinShortEdge >= options.MaxShortEdge
            || options.MinLongEdge >= options.MaxLongEdge
        )
            return ValidateOptionsResult.Fail(InvertedEdgeRangeMessage);

        if (
            options.MinShortEdge > options.MinLongEdge
            || options.MaxShortEdge > options.MaxLongEdge
        )
            return ValidateOptionsResult.Fail(ShortEdgeExceedsLongEdgeMessage);

        if (options.QualityLadder.Count == 0)
            return ValidateOptionsResult.Fail(EmptyQualityLadderMessage);

        if (options.QualityLadder.Any(quality => quality is < 1 or > 100))
            return ValidateOptionsResult.Fail(QualityOutOfRangeMessage);

        var isStrictlyDescending = options
            .QualityLadder.Zip(options.QualityLadder.Skip(1))
            .All(pair => pair.First > pair.Second);

        if (!isStrictlyDescending)
            return ValidateOptionsResult.Fail(UnorderedQualityLadderMessage);

        return options.DownscaleFactor is <= 0 or >= 1
            ? ValidateOptionsResult.Fail(DownscaleFactorOutOfRangeMessage)
            : ValidateOptionsResult.Success;
    }
}
