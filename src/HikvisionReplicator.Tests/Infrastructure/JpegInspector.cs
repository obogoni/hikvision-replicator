using System.Buffers.Binary;
using System.Text;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// Reads a JPEG's marker segments directly, so the tests can ask what the derivative actually
/// contains rather than what an imaging library is willing to tell them. Whether the encoder
/// carried a colour profile or a GPS coordinate forward is a question about bytes.
/// </summary>
internal static class JpegInspector
{
    public const byte App0 = 0xE0;
    public const byte App1 = 0xE1;
    public const byte App2 = 0xE2;
    public const byte BaselineStartOfFrame = 0xC0;
    public const byte ProgressiveStartOfFrame = 0xC2;

    /// <param name="Marker">The second byte of the marker, e.g. 0xE1 for APP1.</param>
    /// <param name="Payload">The segment body, with the two-byte length word already removed.</param>
    public readonly record struct Segment(byte Marker, ReadOnlyMemory<byte> Payload);

    public static IReadOnlyList<Segment> Segments(byte[] jpeg)
    {
        var found = new List<Segment>();
        if (jpeg.Length < 2 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            return found;

        var at = 2;
        while (at + 3 < jpeg.Length)
        {
            if (jpeg[at] != 0xFF)
            {
                at++;
                continue;
            }

            var marker = jpeg[at + 1];
            at += 2;

            // Padding, and the standalone markers that carry no length word.
            if (marker is 0xFF or 0x01 or >= 0xD0 and <= 0xD9)
                continue;

            if (at + 1 >= jpeg.Length)
                break;

            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(at, 2));
            var payloadLength = Math.Min(length - 2, jpeg.Length - at - 2);
            if (payloadLength < 0)
                break;

            found.Add(new Segment(marker, jpeg.AsMemory(at + 2, payloadLength)));

            // Entropy-coded data follows the start-of-scan; there are no further parsable
            // segments worth walking to.
            if (marker == 0xDA)
                break;

            at += length;
        }

        return found;
    }

    public static bool IsStartOfFrame(byte marker) =>
        marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);

    /// <summary>Dimensions and colour-component count, read from the first start-of-frame.</summary>
    public static (byte Marker, int Width, int Height, int Components) Frame(byte[] jpeg)
    {
        foreach (var segment in Segments(jpeg))
        {
            if (!IsStartOfFrame(segment.Marker))
                continue;

            var body = segment.Payload.Span;
            return (
                segment.Marker,
                BinaryPrimitives.ReadUInt16BigEndian(body.Slice(3, 2)),
                BinaryPrimitives.ReadUInt16BigEndian(body.Slice(1, 2)),
                body[5]
            );
        }

        return (0, 0, 0, 0);
    }

    /// <summary>
    /// True when an APPn segment's payload begins with the given identifier — how EXIF
    /// (<c>"Exif"</c>) and embedded colour profiles (<c>"ICC_PROFILE"</c>) announce themselves.
    /// </summary>
    public static bool HasApplicationSegment(byte[] jpeg, string identifier) =>
        Segments(jpeg)
            .Where(segment => segment.Marker is >= 0xE0 and <= 0xEF)
            .Any(segment =>
                segment.Payload.Length >= identifier.Length
                && Encoding.ASCII.GetString(segment.Payload.Span[..identifier.Length])
                    == identifier
            );
}
