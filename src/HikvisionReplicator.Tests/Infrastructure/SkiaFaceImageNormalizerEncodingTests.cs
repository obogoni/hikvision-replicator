using System.Security.Cryptography;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using SkiaSharp;

namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// The byte band, the fingerprint, and the determinism the whole no-op-update guarantee rests on.
///
/// <para>
/// <b>Read this before touching the golden hashes below.</b> They are SHA-256 over the exact
/// derivative bytes this pipeline produces from the committed fixtures. A SkiaSharp upgrade will
/// change the encoder's output and these tests will fail. That is the tests working, not the
/// tests being wrong. The correct response is to review the new derivatives against the spec's
/// criteria — still JPEG, still upright, still inside the 40–200 KB band, still inside the
/// resolution envelope — and then re-record the hashes deliberately, noting the SkiaSharp version
/// that produced them. <b>Never loosen the assertion.</b> A golden hash that has been relaxed
/// into "some hash" is the one thing that could let a silent change in normalization output ship
/// unnoticed, and USR-26's promise that a byte-identical re-upload does not touch
/// <c>UpdatedAt</c> would go with it.
/// </para>
///
/// <para>Recorded against SkiaSharp 3.119.4.</para>
/// </summary>
public class SkiaFaceImageNormalizerEncodingTests
{
    private const string ExifRotatedPortraitHash =
        "3a38f9e543ee913b1fca4b6ea66df5c4beebfce0283e0d76234e71768ec981c1";
    private const string LargeFractalHash =
        "819853212879502ce542045c6659fd9dd57d0b48caeead28dd40f84d2c148b13";
    private const string PngHash =
        "c8452e29974e85389560e6536084cb63ac177d4fe2948679485d875eec55bd41";
    private const string GrayscaleHash =
        "f29ceea0511dd8a47d24856f6a9ed568cd77b1b67d52d1ae3ec19e1eb3ce52e7";
    private const string ProgressiveHash =
        "d58acc6f14641a04f1767e8ca08b8795db09de307c2cc628bace5b17dcc029ab";
    private const string IccProfiledHash =
        "eff9a48f69603fb621599ccf73a88f66f347d7d5e2c8cf0ceb00613f5399eff7";
    private const string GpsTaggedHash =
        "73b05c5e63de8073066e0a958eed77d02707d16a8962e77ad20fcefbcf86fbd2";

    private static NormalizedFaceImage Normalize(string fixture) =>
        FaceImageNormalizerFactory.Build().Normalize(FaceFixtures.Bytes(fixture)).AsT0;

    // ─── USR-15: the band, both ends of it ──

    [Theory]
    [InlineData(FaceFixtures.ExifRotatedPortrait)]
    [InlineData(FaceFixtures.LargeFractal)]
    [InlineData(FaceFixtures.Png)]
    [InlineData(FaceFixtures.Grayscale)]
    [InlineData(FaceFixtures.Progressive)]
    [InlineData(FaceFixtures.IccProfiled)]
    [InlineData(FaceFixtures.GpsTagged)]
    public void Every_photograph_is_stored_between_forty_and_two_hundred_kilobytes(string fixture)
    {
        var stored = Normalize(fixture).Content;

        // The lower bound is asserted in its own right. An upper-bound-only pipeline is a
        // different pipeline: over-compression is a device rejection cause, so a 12 KB derivative
        // is as much a failure as a 500 KB one.
        Assert.True(
            stored.Length >= 40 * 1024,
            $"{fixture} was stored at {stored.Length} bytes, under the 40 KB minimum"
        );
        Assert.True(
            stored.Length <= 200 * 1024,
            $"{fixture} was stored at {stored.Length} bytes, over the 200 KB maximum"
        );
    }

    [Fact]
    public void Photograph_still_too_large_at_the_lowest_quality_is_shrunk_until_it_fits()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.LargeFractal);

        // At the shipped 200 KB ceiling this fixture lands on the ladder alone, at the
        // resolution the envelope allows. Tightening the byte ceiling is what forces the other
        // branch: no quality reaches it at that resolution, so the image itself has to shrink.
        var atCeiling = FaceImageNormalizerFactory.Build().Normalize(upload).AsT0;
        var shrunk = FaceImageNormalizerFactory
            .Build(o => o.MaxByteSize = 60 * 1024)
            .Normalize(upload)
            .AsT0;

        Assert.True(
            shrunk.Width < atCeiling.Width && shrunk.Height < atCeiling.Height,
            $"expected a smaller image than {atCeiling.Width}x{atCeiling.Height}, "
                + $"got {shrunk.Width}x{shrunk.Height} — the downscale branch never ran"
        );
        Assert.InRange(shrunk.Content.Length, 40 * 1024, 60 * 1024);
    }

    [Fact]
    public void Photograph_too_uniform_to_reach_the_minimum_size_is_rejected()
    {
        // 640x480 of flat grey. It clears every other guard and then cannot reach 40 KB at any
        // quality — which is the useful signal that it is a lens cap and not a face.
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.NearUniform));

        var error = result.AsT1;
        Assert.Equal(SkiaFaceImageNormalizer.Errors.Field, error.Field);
        Assert.Equal(SkiaFaceImageNormalizer.Errors.CannotReachMinimumSize, error.Message);
    }

    [Fact]
    public void Photograph_too_uniform_to_reach_the_minimum_size_is_never_stored_below_the_band()
    {
        var result = FaceImageNormalizerFactory
            .Build()
            .Normalize(FaceFixtures.Bytes(FaceFixtures.NearUniform));

        // Storing it under the band would hand the device a file it rejects at enrolment, which
        // is a failure discovered at a turnstile instead of at the API.
        Assert.False(result.IsT0);
    }

    [Fact]
    public void Photograph_that_cannot_reach_the_band_without_falling_below_the_floor_is_rejected()
    {
        // A byte ceiling this tight cannot be met at any quality until the image is smaller than
        // the device's resolution minimum. Shrinking past the floor is not on offer, so the
        // request is refused rather than a non-compliant derivative stored.
        var result = FaceImageNormalizerFactory
            .Build(o =>
            {
                o.MinByteSize = 1024;
                o.MaxByteSize = 5 * 1024;
            })
            .Normalize(FaceFixtures.Bytes(FaceFixtures.LargeFractal));

        var error = result.AsT1;
        Assert.Equal(SkiaFaceImageNormalizer.Errors.Field, error.Field);
        Assert.Equal(SkiaFaceImageNormalizer.Errors.CannotReachMaximumSize, error.Message);
    }

    // ─── USR-14: nothing from the source travels with the derivative ──

    [Fact]
    public void Derivative_carries_none_of_the_source_metadata()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.GpsTagged);
        Assert.True(
            JpegInspector.HasApplicationSegment(upload, "Exif"),
            "the fixture must actually carry EXIF for this to prove anything"
        );

        var derivative = Normalize(FaceFixtures.GpsTagged).Content;

        Assert.False(JpegInspector.HasApplicationSegment(derivative, "Exif"));
    }

    [Fact]
    public void Derivative_of_a_location_tagged_photograph_carries_no_location()
    {
        // Not "no EXIF segment" but "not these bytes anywhere": the fixture's GPS block is
        // searched for across the whole derivative, so it cannot survive in some other segment.
        var upload = FaceFixtures.Bytes(FaceFixtures.GpsTagged);
        var sourceExif = JpegInspector
            .Segments(upload)
            .First(segment => segment.Marker == JpegInspector.App1)
            .Payload.ToArray();

        var derivative = Normalize(FaceFixtures.GpsTagged).Content;

        Assert.Equal(-1, IndexOf(derivative, sourceExif));
    }

    // ─── USR-22: the fingerprint describes the stored bytes ──

    [Fact]
    public void Recorded_hash_is_the_sha256_of_the_stored_bytes()
    {
        var normalized = Normalize(FaceFixtures.Png);

        var independently = Convert.ToHexStringLower(SHA256.HashData(normalized.Content));

        Assert.Equal(independently, normalized.ContentHash);
    }

    [Fact]
    public void Recorded_dimensions_are_the_dimensions_of_the_stored_bytes()
    {
        var normalized = Normalize(FaceFixtures.LargeFractal);

        using var stored = SKBitmap.Decode(normalized.Content);

        Assert.Equal(stored.Width, normalized.Width);
        Assert.Equal(stored.Height, normalized.Height);
    }

    // ─── Determinism: the invariant USR-26 stands on ──

    [Fact]
    public void Normalizing_the_same_photograph_twice_produces_identical_bytes()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.LargeFractal);

        var first = FaceImageNormalizerFactory.Build().Normalize(upload).AsT0;
        var second = FaceImageNormalizerFactory.Build().Normalize(upload).AsT0;

        // The large fixture is the one that walks furthest down the ladder, so it is the one a
        // convergence-based search would resolve differently between runs.
        Assert.Equal(first.Content, second.Content);
    }

    [Fact]
    public void Normalizing_the_same_photograph_twice_produces_an_identical_hash()
    {
        var upload = FaceFixtures.Bytes(FaceFixtures.GpsTagged);

        var first = FaceImageNormalizerFactory.Build().Normalize(upload).AsT0;
        var second = FaceImageNormalizerFactory.Build().Normalize(upload).AsT0;

        // An unchanged hash is what lets a re-upsert leave UpdatedAt alone (USR-26).
        Assert.Equal(first.ContentHash, second.ContentHash);
    }

    // ─── Golden hashes — see the note at the top of this file before changing any of these ──

    [Theory]
    [InlineData(FaceFixtures.ExifRotatedPortrait, ExifRotatedPortraitHash)]
    [InlineData(FaceFixtures.LargeFractal, LargeFractalHash)]
    [InlineData(FaceFixtures.Png, PngHash)]
    [InlineData(FaceFixtures.Grayscale, GrayscaleHash)]
    [InlineData(FaceFixtures.Progressive, ProgressiveHash)]
    [InlineData(FaceFixtures.IccProfiled, IccProfiledHash)]
    [InlineData(FaceFixtures.GpsTagged, GpsTaggedHash)]
    public void Photograph_normalizes_to_the_derivative_recorded_for_it(
        string fixture,
        string expectedHash
    )
    {
        Assert.Equal(expectedHash, Normalize(fixture).ContentHash);
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var at = 0; at + needle.Length <= haystack.Length; at++)
        {
            if (haystack.AsSpan(at, needle.Length).SequenceEqual(needle))
                return at;
        }

        return -1;
    }
}
