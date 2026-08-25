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

        // USR-17. Checked against the *oriented* dimensions: a 90 or 270 degree origin swaps
        // width and height, so a portrait photograph stored as landscape pixels would otherwise
        // be judged as the landscape image it is not.
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

        return new NormalizedFaceImage(upload, Sha256Hex(upload), width, height);
    }

    /// <summary>
    /// The dimensions the image is meant to be *seen* at. SkiaSharp does not auto-orient, so
    /// every origin is handled explicitly rather than by falling through a default.
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
