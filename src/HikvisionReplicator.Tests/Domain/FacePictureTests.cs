using System.Reflection;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Tests.Domain;

public class FacePictureTests
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x01, 0x02];
    private static readonly byte[] ReplacementJpeg = [0xFF, 0xD8, 0xFF, 0xE1, 0x0A, 0x0B];

    // ─── USR-10: a picture always carries content ───

    [Fact]
    public void Picture_holds_the_content_it_was_made_from()
    {
        var picture = FacePicture.ForUser(Jpeg);

        Assert.Equal(Jpeg, picture.Content);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Picture_made_from_no_content_is_rejected(bool useNull)
    {
        var content = useNull ? null : Array.Empty<byte>();

        var error = Assert.Throws<ArgumentException>(() => FacePicture.ForUser(content!));

        Assert.Contains(FacePicture.EmptyContent, error.Message, StringComparison.Ordinal);
    }

    // ─── USR-25: supplying a picture replaces the stored one ───

    [Fact]
    public void Replaced_picture_holds_the_new_content()
    {
        var picture = FacePicture.ForUser(Jpeg);

        picture.Replace(ReplacementJpeg);

        Assert.Equal(ReplacementJpeg, picture.Content);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Replacing_a_picture_with_no_content_is_rejected_and_leaves_it_unchanged(
        bool useNull
    )
    {
        var picture = FacePicture.ForUser(Jpeg);
        var content = useNull ? null : Array.Empty<byte>();

        var error = Assert.Throws<ArgumentException>(() => picture.Replace(content!));

        Assert.Contains(FacePicture.EmptyContent, error.Message, StringComparison.Ordinal);
        Assert.Equal(Jpeg, picture.Content);
    }

    // ─── It is reachable only through its user, never through IRepository<T> ───

    [Fact]
    public void Face_picture_is_not_an_aggregate_root()
    {
        Assert.False(typeof(IAggregateRoot).IsAssignableFrom(typeof(FacePicture)));
    }

    [Fact]
    public void Face_picture_can_be_rehydrated_by_the_persistence_layer()
    {
        var parameterless = typeof(FacePicture).GetConstructor(
            BindingFlags.NonPublic | BindingFlags.Instance,
            binder: null,
            Type.EmptyTypes,
            modifiers: null
        );

        Assert.NotNull(parameterless);
        Assert.True(parameterless.IsPrivate);
    }
}
