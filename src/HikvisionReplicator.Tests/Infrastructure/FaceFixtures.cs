namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// The face-picture fixture bank as the tests see it. The files live in
/// <c>tests/assets</c> and are linked into this project's output as <c>assets/</c>;
/// <c>tests/assets/PROVENANCE.md</c> records how each one is generated and what it exercises.
/// </summary>
internal static class FaceFixtures
{
    public const string ExifRotatedPortrait = "exif-rotated-portrait.jpg";
    public const string LargeFractal = "large-fractal.jpg";
    public const string SubFloorThumbnail = "sub-floor-thumbnail.jpg";
    public const string Png = "plain.png";
    public const string Grayscale = "grayscale.jpg";
    public const string Progressive = "progressive.jpg";
    public const string IccProfiled = "icc-profiled.jpg";
    public const string GpsTagged = "gps-tagged.jpg";
    public const string DecodeBomb = "decode-bomb.png";
    public const string NearUniform = "near-uniform.jpg";
    public const string Cmyk = "cmyk.jpg";
    public const string SinglePixel = "single-pixel.jpg";
    public const string NotAnImage = "not-an-image.bin";

    /// <summary>
    /// The fixtures that carry photographic entropy and are expected to normalize
    /// successfully. The sub-floor, single-pixel, decode-bomb, near-uniform and not-an-image
    /// fixtures are deliberately absent: each one exists to be rejected.
    /// </summary>
    public static readonly string[] Photographic =
    [
        ExifRotatedPortrait,
        LargeFractal,
        Png,
        Grayscale,
        Progressive,
        IccProfiled,
        GpsTagged,
        Cmyk,
    ];

    public static string PathTo(string fixture) =>
        Path.Combine(AppContext.BaseDirectory, "assets", fixture);

    public static byte[] Bytes(string fixture) => File.ReadAllBytes(PathTo(fixture));
}
