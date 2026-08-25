namespace HikvisionReplicator.Tests.Infrastructure;

/// <summary>
/// The bank is the ground the normalizer's whole test suite stands on. If a fixture stops being
/// copied to the output directory, or is silently replaced by one with different dimensions or a
/// different format, every normalizer test downstream starts asserting something other than what
/// it claims to assert. This is the test that notices.
/// <para>
/// Headers are parsed here rather than read through an imaging library on purpose: the point is
/// to confirm the committed bytes are what <c>PROVENANCE.md</c> says they are, independently of
/// whatever the code under test happens to believe.
/// </para>
/// </summary>
public class FaceFixtureBankTests
{
    [Theory]
    [InlineData(FaceFixtures.ExifRotatedPortrait, "JPEG", 1200, 900)]
    [InlineData(FaceFixtures.LargeFractal, "JPEG", 4000, 3000)]
    [InlineData(FaceFixtures.SubFloorThumbnail, "JPEG", 320, 240)]
    [InlineData(FaceFixtures.Png, "PNG", 1200, 900)]
    [InlineData(FaceFixtures.Grayscale, "JPEG", 1200, 900)]
    [InlineData(FaceFixtures.Progressive, "JPEG", 1200, 900)]
    [InlineData(FaceFixtures.IccProfiled, "JPEG", 1200, 900)]
    [InlineData(FaceFixtures.GpsTagged, "JPEG", 1200, 900)]
    [InlineData(FaceFixtures.DecodeBomb, "PNG", 30000, 30000)]
    [InlineData(FaceFixtures.NearUniform, "JPEG", 640, 480)]
    [InlineData(FaceFixtures.NotAnImage, "none", 0, 0)]
    public void Declared_fixture_is_present_and_carries_the_format_and_dimensions_it_claims(
        string fixture,
        string expectedFormat,
        int expectedWidth,
        int expectedHeight
    )
    {
        var path = FaceFixtures.PathTo(fixture);

        Assert.True(File.Exists(path), $"{fixture} was not copied to {path}");

        var bytes = FaceFixtures.Bytes(fixture);
        Assert.NotEmpty(bytes);

        var header = FixtureHeader.Read(bytes);
        Assert.Equal(expectedFormat, header.Format);
        Assert.Equal(expectedWidth, header.Width);
        Assert.Equal(expectedHeight, header.Height);
    }
}
