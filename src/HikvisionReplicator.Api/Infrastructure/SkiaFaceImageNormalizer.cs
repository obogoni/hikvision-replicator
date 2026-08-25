using System.Security.Cryptography;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Options;
using OneOf;
using SkiaSharp;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// Turns an upload into a JPEG the device will enrol, or explains why it cannot.
/// <para>
/// The guard order is load-bearing, not stylistic. Every cheap refusal happens before the
/// expensive one it protects: the byte cap before a codec is constructed, the declared-pixel cap
/// before a decode buffer is allocated. An attacker-supplied image reaches an allocation only
/// after we have decided the allocation is affordable.
/// </para>
/// </summary>
public sealed class SkiaFaceImageNormalizer : IFaceImageNormalizer
{
    private readonly FaceImageOptions _options;

    public SkiaFaceImageNormalizer(IOptions<FaceImageOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
    }

    public OneOf<NormalizedFaceImage, ValidationError> Normalize(byte[] upload)
    {
        if (upload is null || upload.Length == 0)
            return Reject(Errors.NotDecodable);

        // USR-19. First, and before any codec exists: an 8 MB cap is worthless if the thing it
        // guards has already run.
        if (upload.Length > _options.MaxUploadBytes)
            return Reject(Errors.UploadTooLarge);

        using var stream = new MemoryStream(upload, writable: false);
        using var codec = SKCodec.Create(stream);

        // USR-21.
        if (codec is null)
            return Reject(Errors.NotDecodable);

        var encoded = codec.Info;

        // USR-20. SKCodec parses the header only, so Width and Height are available here without
        // a single pixel having been decoded. A 68-byte file declaring 30000x30000 asks for a
        // 3.6 GB buffer; this is the line that refuses to allocate it.
        if ((long)encoded.Width * encoded.Height > _options.MaxDecodePixels)
            return Reject(Errors.TooManyPixels);

        var (width, height) = Orient(encoded.Width, encoded.Height, codec.EncodedOrigin);

        // USR-17. Checked against the *oriented* dimensions: a quarter-turn origin swaps width
        // and height, so a portrait photograph stored as landscape pixels would otherwise be
        // judged as the landscape image it is not. And never upscaled: manufacturing a compliant
        // file out of one that is too small produces something no device can recognise.
        if (
            Math.Min(width, height) < _options.MinShortEdge
            || Math.Max(width, height) < _options.MinLongEdge
        )
            return Reject(
                Errors.BelowResolutionFloor(
                    _options.MinShortEdge,
                    _options.MinLongEdge,
                    width,
                    height
                )
            );

        using var upright = DecodeUpright(codec, encoded);
        if (upright is null)
            return Reject(Errors.NotDecodable);

        using var fitted = FitToCeiling(upright);

        return EncodeIntoBand(fitted)
            .Match<OneOf<NormalizedFaceImage, ValidationError>>(
                landed => new NormalizedFaceImage(
                    landed.Content,
                    Sha256Hex(landed.Content),
                    landed.Width,
                    landed.Height
                ),
                error => error
            );
    }

    /// <summary>
    /// Walks the quality ladder for the byte band (USR-15), shrinking the image and walking it
    /// again while the whole ladder is still over the ceiling.
    /// <para>
    /// <b>A fixed ladder, deliberately, and never a bisection search.</b> USR-26 requires that
    /// re-sending an identical upload leaves <c>UpdatedAt</c> untouched, which holds only if
    /// identical input bytes produce identical output bytes and therefore an identical hash. A
    /// search converges on different quality values from different starting conditions; a ladder
    /// walked in a fixed order from a fixed start cannot.
    /// </para>
    /// <para>
    /// The ladder descends, so the first quality inside the ceiling is also the highest one
    /// available. If even that lands under <see cref="FaceImageOptions.MinByteSize"/> there is no
    /// quality that reaches the band at these dimensions, and shrinking would only make it
    /// smaller — the image is nearly uniform, which is a lens cap rather than a face.
    /// </para>
    /// </summary>
    private OneOf<(byte[] Content, int Width, int Height), ValidationError> EncodeIntoBand(
        SKBitmap fitted
    )
    {
        var current = fitted.Copy();
        try
        {
            while (true)
            {
                var candidate = HighestQualityUnderCeiling(current);

                if (candidate is not null)
                {
                    return candidate.Length >= _options.MinByteSize
                        ? (candidate, current.Width, current.Height)
                        : Reject(Errors.CannotReachMinimumSize);
                }

                var next = ScaleBy(current, _options.DownscaleFactor);
                if (
                    Math.Min(next.Width, next.Height) < _options.MinShortEdge
                    || Math.Max(next.Width, next.Height) < _options.MinLongEdge
                )
                {
                    next.Dispose();
                    return Reject(Errors.CannotReachMaximumSize);
                }

                current.Dispose();
                current = next;
            }
        }
        finally
        {
            current.Dispose();
        }
    }

    private byte[]? HighestQualityUnderCeiling(SKBitmap bitmap)
    {
        foreach (var quality in _options.QualityLadder)
        {
            var encoded = Encode(bitmap, quality);
            if (encoded.Length <= _options.MaxByteSize)
                return encoded;
        }

        return null;
    }

    /// <summary>
    /// The dimensions the image is meant to be <em>seen</em> at. SkiaSharp does not auto-orient,
    /// so every origin is listed explicitly rather than reached through a default.
    /// </summary>
    internal static (int Width, int Height) Orient(int width, int height, SKEncodedOrigin origin) =>
        origin switch
        {
            SKEncodedOrigin.TopLeft => (width, height),
            SKEncodedOrigin.TopRight => (width, height),
            SKEncodedOrigin.BottomRight => (width, height),
            SKEncodedOrigin.BottomLeft => (width, height),
            SKEncodedOrigin.LeftTop => (height, width),
            SKEncodedOrigin.RightTop => (height, width),
            SKEncodedOrigin.RightBottom => (height, width),
            SKEncodedOrigin.LeftBottom => (height, width),
            _ => (width, height),
        };

    /// <summary>
    /// Decodes into sRGB and then applies the EXIF origin to the pixels (USR-13), because the
    /// metadata that recorded the rotation is about to be discarded and the rotation has to
    /// survive it.
    /// </summary>
    private static SKBitmap? DecodeUpright(SKCodec codec, SKImageInfo encoded)
    {
        // One colour space regardless of what the source declared, so a grayscale, CMYK or
        // oddly-profiled photograph converges here rather than reaching the encoder as something
        // the device may not decode.
        var target = Rgba(encoded.Width, encoded.Height);
        var decoded = new SKBitmap(target);

        var read = codec.GetPixels(target, decoded.GetPixels());
        if (read is not (SKCodecResult.Success or SKCodecResult.IncompleteInput))
        {
            decoded.Dispose();
            return null;
        }

        if (codec.EncodedOrigin == SKEncodedOrigin.TopLeft)
            return decoded;

        using (decoded)
            return ApplyOrigin(decoded, codec.EncodedOrigin);
    }

    /// <summary>
    /// Redraws the bitmap under the transform its EXIF origin describes.
    /// <para>
    /// Written as an explicit matrix per origin rather than a sequence of canvas operations: a
    /// wrong sign here produces a mirrored face that still passes any check that looks only at
    /// dimensions, so the mapping is stated in the form it can be read back in. Each matrix sends
    /// source pixel (x, y) to the place the EXIF standard says it should be seen, where
    /// <c>w</c> and <c>h</c> are the source's own width and height.
    /// </para>
    /// </summary>
    private static SKBitmap ApplyOrigin(SKBitmap source, SKEncodedOrigin origin)
    {
        float w = source.Width;
        float h = source.Height;

        // SKMatrix is (scaleX, skewX, transX, skewY, scaleY, transY, ...), applied as
        // x' = scaleX*x + skewX*y + transX and y' = skewY*x + scaleY*y + transY.
        var matrix = origin switch
        {
            // Mirrored horizontally: x' = w - x.
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            // Half turn.
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            // Mirrored vertically: y' = h - y.
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            // Transposed about the main diagonal: x' = y, y' = x.
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            // A quarter turn clockwise: x' = h - y, y' = x.
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, h, 1, 0, 0, 0, 0, 1),
            // Transposed about the anti-diagonal.
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, h, -1, 0, w, 0, 0, 1),
            // A quarter turn anticlockwise: x' = y, y' = w - x.
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, w, 0, 0, 1),
            SKEncodedOrigin.TopLeft => SKMatrix.Identity,
            _ => SKMatrix.Identity,
        };

        var (width, height) = Orient(source.Width, source.Height, origin);
        var rotated = new SKBitmap(Rgba(width, height));

        using var canvas = new SKCanvas(rotated);
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        canvas.Flush();
        return rotated;
    }

    /// <summary>
    /// Scales the image down until it fits inside the ceiling (USR-16), preserving the source
    /// aspect ratio and cropping nothing (USR-18). Never scales up — an image already inside the
    /// ceiling is handed back as it is.
    /// </summary>
    private SKBitmap FitToCeiling(SKBitmap source)
    {
        var shorter = Math.Min(source.Width, source.Height);
        var longer = Math.Max(source.Width, source.Height);

        if (shorter <= _options.MaxShortEdge && longer <= _options.MaxLongEdge)
            return source.Copy();

        return ScaleBy(
            source,
            Math.Min(
                (double)_options.MaxShortEdge / shorter,
                (double)_options.MaxLongEdge / longer
            )
        );
    }

    /// <summary>
    /// One uniform scale applied to both edges. That, and nothing else, is what preserves the
    /// aspect ratio and guarantees nothing is cropped.
    /// </summary>
    internal static SKBitmap ScaleBy(SKBitmap source, double scale)
    {
        var width = Math.Max(1, (int)Math.Round(source.Width * scale, MidpointRounding.AwayFromZero));
        var height = Math.Max(
            1,
            (int)Math.Round(source.Height * scale, MidpointRounding.AwayFromZero)
        );

        // Mitchell cubic resampling: fixed coefficients, so the same input always yields the same
        // pixels. SKFilterQuality is gone in SkiaSharp 3.
        return source.Resize(Rgba(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
            ?? throw new InvalidOperationException("Resizing the face picture failed.");
    }

    /// <summary>
    /// Baseline JPEG. Re-encoding is also what strips the source's metadata: SkiaSharp's encoder
    /// writes no EXIF, so the GPS coordinates of wherever the photograph was taken do not survive
    /// this call (USR-14).
    /// </summary>
    internal static byte[] Encode(SKBitmap bitmap, int quality)
    {
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    private static SKImageInfo Rgba(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, SKColorSpace.CreateSrgb());

    internal static string Sha256Hex(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static ValidationError Reject(string message) => new(Errors.Field, message);

    public static class Errors
    {
        public const string Field = "facePicture";

        public const string NotDecodable = "Face picture is not a decodable image.";

        public const string UploadTooLarge =
            "Face picture upload is larger than the accepted maximum.";

        public const string TooManyPixels =
            "Face picture declares more pixels than can safely be decoded.";

        public const string CannotReachMinimumSize =
            "Face picture has too little detail to store at the required quality. "
            + "Supply a sharper photograph.";

        public const string CannotReachMaximumSize =
            "Face picture cannot be compressed into the accepted size without falling below "
            + "the minimum resolution.";

        public static string BelowResolutionFloor(
            int minShortEdge,
            int minLongEdge,
            int width,
            int height
        ) =>
            $"Face picture must be at least {minShortEdge} pixels on its shorter edge and "
            + $"{minLongEdge} pixels on its longer edge; it is {width} by {height}.";
    }
}
