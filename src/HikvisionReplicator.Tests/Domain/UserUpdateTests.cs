using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class UserUpdateTests
{
    private static readonly DateTime RegisteredOn = new(2026, 8, 25, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 8, 25, 19, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] StoredContent = [0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22];
    private static readonly byte[] ReplacementContent = [0xFF, 0xD8, 0xFF, 0xE1, 0x33, 0x44];

    private static readonly FaceFingerprint StoredFace = FaceFingerprint
        .Create("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", 81_920, 720, 960)
        .AsT0;

    private static readonly FaceFingerprint ReplacementFace = FaceFingerprint
        .Create("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae", 61_440, 640, 480)
        .AsT0;

    private static User Registered() =>
        User.Create("TICKET-1", "Ada Lovelace", "004215", StoredFace, StoredContent, RegisteredOn)
            .AsT0;

    private static void AssertPictureIsUntouched(User user)
    {
        Assert.Equal(StoredFace, user.Face);
        Assert.NotNull(user.Picture);
        Assert.Equal(StoredContent, user.Picture.Content);
    }

    // ─── USR-23 / USR-24: a correction without a picture keeps the stored image ───

    [Fact]
    public void Correcting_the_name_leaves_every_other_value_unchanged()
    {
        var user = Registered();

        var result = user.Update("Ada King", "004215", null, null, Later);

        Assert.True(result.IsT0);
        Assert.Equal("Ada King", user.Name);
        Assert.Equal("004215", user.AccessCode.Value);
        Assert.Equal("TICKET-1", user.ExternalRef.Value);
        Assert.Equal(RegisteredOn, user.CreatedAt);
        AssertPictureIsUntouched(user);
    }

    [Fact]
    public void Update_omitting_the_face_picture_leaves_the_image_hash_size_and_dimensions_intact()
    {
        var user = Registered();

        user.Update("Ada King", "004215", null, null, Later);

        Assert.Equal(StoredFace.ContentHash, user.Face.ContentHash);
        Assert.Equal(81_920, user.Face.ByteSize);
        Assert.Equal(720, user.Face.Width);
        Assert.Equal(960, user.Face.Height);
        Assert.NotNull(user.Picture);
        Assert.Equal(StoredContent, user.Picture.Content);
    }

    // ─── USR-25: a supplied picture replaces the image and its recorded fingerprint ───

    [Fact]
    public void Update_supplying_a_face_picture_replaces_the_image_and_its_fingerprint()
    {
        var user = Registered();

        var result = user.Update(
            "Ada Lovelace",
            "004215",
            ReplacementFace,
            ReplacementContent,
            Later
        );

        Assert.True(result.IsT0);
        Assert.Equal(ReplacementFace, user.Face);
        Assert.NotNull(user.Picture);
        Assert.Equal(ReplacementContent, user.Picture.Content);
        Assert.Equal(Later, user.UpdatedAt);
    }

    // ─── USR-26: the update time moves only when something actually differs ───

    [Fact]
    public void Update_repeating_the_stored_values_does_not_advance_the_update_time()
    {
        var user = Registered();

        var result = user.Update("Ada Lovelace", "004215", null, null, Later);

        Assert.True(result.IsT0);
        Assert.Equal(RegisteredOn, user.UpdatedAt);
    }

    [Fact]
    public void Update_resupplying_the_stored_picture_does_not_advance_the_update_time()
    {
        var user = Registered();

        var result = user.Update("Ada Lovelace", "004215", StoredFace, StoredContent, Later);

        Assert.True(result.IsT0);
        Assert.Equal(StoredFace.ContentHash, user.Face.ContentHash);
        Assert.Equal(RegisteredOn, user.UpdatedAt);
    }

    [Fact]
    public void Update_differing_only_in_surrounding_whitespace_does_not_advance_the_update_time()
    {
        var user = Registered();

        user.Update("  Ada Lovelace  ", "004215", null, null, Later);

        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Equal(RegisteredOn, user.UpdatedAt);
    }

    [Fact]
    public void Changing_only_the_access_code_advances_the_update_time()
    {
        var user = Registered();

        var result = user.Update("Ada Lovelace", "778899", null, null, Later);

        Assert.True(result.IsT0);
        Assert.Equal("778899", user.AccessCode.Value);
        Assert.Equal(Later, user.UpdatedAt);
    }

    // ─── USR-03 / USR-04: an update carries the same field rules as a registration ───

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Update_without_a_usable_name_is_rejected(string? name)
    {
        var user = Registered();

        var result = user.Update(name, "004215", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Update_with_a_name_longer_than_the_permitted_length_is_rejected()
    {
        var user = Registered();

        var result = user.Update(new string('A', 101), "004215", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameTooLong, result.AsT1.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("12a4")]
    [InlineData("٤٥٦٧")]
    [InlineData("123")]
    public void Update_without_a_usable_access_code_is_rejected(string? accessCode)
    {
        var user = Registered();

        var result = user.Update("Ada Lovelace", accessCode, null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
    }

    // ─── USR-27: a rejected update applies nothing at all ───

    [Fact]
    public void Rejected_update_leaves_the_name_unchanged()
    {
        var user = Registered();

        var result = user.Update("Ada King", "not-a-code", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("Ada Lovelace", user.Name);
    }

    [Fact]
    public void Rejected_update_leaves_the_access_code_unchanged()
    {
        var user = Registered();

        var result = user.Update("", "778899", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("004215", user.AccessCode.Value);
    }

    [Fact]
    public void Rejected_update_leaves_the_stored_picture_and_its_fingerprint_unchanged()
    {
        var user = Registered();

        var result = user.Update(
            "Ada King",
            "not-a-code",
            ReplacementFace,
            ReplacementContent,
            Later
        );

        Assert.True(result.IsT1);
        AssertPictureIsUntouched(user);
    }

    [Fact]
    public void Rejected_update_does_not_advance_the_update_time()
    {
        var user = Registered();

        var result = user.Update("Ada King", "not-a-code", ReplacementFace, ReplacementContent, Later);

        Assert.True(result.IsT1);
        Assert.Equal(RegisteredOn, user.UpdatedAt);
    }

    [Fact]
    public void Rejected_update_leaves_the_user_active()
    {
        var user = Registered();

        var result = user.Update("Ada King", "not-a-code", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Null(user.DeletedAt);
    }
}
