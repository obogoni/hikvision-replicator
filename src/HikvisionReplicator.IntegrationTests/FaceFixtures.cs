namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The face-picture fixture bank as the integration tests see it. The files live in
/// <c>tests/assets</c> and are linked into this project's output as <c>assets/</c>;
/// <c>tests/assets/PROVENANCE.md</c> records how each one is generated and what it exercises.
/// </summary>
internal static class FaceFixtures
{
    public const string Portrait = "exif-rotated-portrait.jpg";
    public const string Progressive = "progressive.jpg";
    public const string SubFloorThumbnail = "sub-floor-thumbnail.jpg";
    public const string NotAnImage = "not-an-image.bin";

    public static byte[] Bytes(string fixture) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "assets", fixture));
}

/// <summary>
/// A clock the test controls, so the timestamps a request writes can be asserted against a value
/// the test chose rather than against "roughly now" (AD-023, USR-11).
/// </summary>
internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}
