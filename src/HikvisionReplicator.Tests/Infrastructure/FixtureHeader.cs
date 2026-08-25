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

    /// <summary>Walk the marker segments to the start-of-frame, which is what carries the size.</summary>
    private static (string, int, int) ReadJpeg(byte[] bytes)
    {
        var span = bytes.AsSpan();
        var at = 2;
        while (at + 3 < span.Length)
        {
            if (span[at] != 0xFF)
            {
                at++;
                continue;
            }

            var marker = span[at + 1];
            at += 2;

            // Padding, and the standalone markers that carry no length word.
            if (marker is 0xFF or 0x01 or >= 0xD0 and <= 0xD9)
                continue;

            if (at + 1 >= span.Length)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(span.Slice(at, 2));

            // Every SOFn except the arithmetic-coding and hierarchical outliers (0xC4, 0xC8, 0xCC).
            var isStartOfFrame =
                marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

            if (isStartOfFrame && at + 7 < span.Length)
                return (
                    Jpeg,
                    BinaryPrimitives.ReadUInt16BigEndian(span.Slice(at + 5, 2)),
                    BinaryPrimitives.ReadUInt16BigEndian(span.Slice(at + 3, 2))
                );

            at += length;
        }

        return (Jpeg, 0, 0);
    }
}
