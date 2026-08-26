using System.Buffers.Binary;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// A deliberately tiny, dependency-free reader for the two container formats the fixture bank
/// uses. It exists so <see cref="FaceFixtureBankTests"/> can check the committed bytes against
/// <c>PROVENANCE.md</c> without going through the same imaging library the normalizer uses —
/// a fixture check that trusted the code under test would prove nothing.
/// </summary>
internal static class FixtureHeader
{
    public const string Jpeg = "JPEG";
    public const string Png = "PNG";
    public const string None = "none";

    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    public static (string Format, int Width, int Height) Read(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        if (bytes.AsSpan().StartsWith(PngSignature))
            return ReadPng(bytes);

        return bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xD8
            ? ReadJpeg(bytes)
            : (None, 0, 0);
    }

    /// <summary>IHDR is always the first chunk: 8-byte signature, 4-byte length, 4-byte type.</summary>
    private static (string, int, int) ReadPng(byte[] bytes)
    {
        var span = bytes.AsSpan();
        if (span.Length < 24 || !span.Slice(12, 4).SequenceEqual("IHDR"u8))
            return (Png, 0, 0);

        return (
            Png,
            BinaryPrimitives.ReadInt32BigEndian(span.Slice(16, 4)),
            BinaryPrimitives.ReadInt32BigEndian(span.Slice(20, 4))
        );
    }

    /// <summary>The size lives in the start-of-frame segment, not in the file header.</summary>
    private static (string, int, int) ReadJpeg(byte[] bytes)
    {
        var (_, width, height, _) = JpegInspector.Frame(bytes);
        return (Jpeg, width, height);
    }
}
