using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Tests.Domain;

public class UserCreateTests
{
    private static readonly DateTime Now = new(2026, 8, 25, 18, 45, 0, DateTimeKind.Utc);
    private static readonly byte[] PictureContent = [0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22];

    private static readonly FaceFingerprint Fingerprint = FaceFingerprint
        .Create("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", 81_920, 720, 960)
        .AsT0;

    private static OneOf.OneOf<User, ValidationError> Create(
        string? externalRef = "TICKET-1",
        string? name = "Ada Lovelace",
        string? accessCode = "004215"
    ) => User.Create(externalRef, name, accessCode, Fingerprint, PictureContent, Now);

    // ─── USR-01: a valid registration produces the spectator ───

    [Fact]
    public void User_is_created_from_the_supplied_values()
    {
        var result = Create();

        Assert.True(result.IsT0);
        var user = result.AsT0;
        Assert.Equal("TICKET-1", user.ExternalRef.Value);
        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Equal("004215", user.AccessCode.Value);
        Assert.Equal(Fingerprint, user.Face);
    }

    [Fact]
    public void Created_user_holds_the_supplied_face_picture()
    {
        var user = Create().AsT0;

        Assert.NotNull(user.Picture);
        Assert.Equal(PictureContent, user.Picture.Content);
    }

    [Fact]
    public void Created_user_is_active()
    {
        var user = Create().AsT0;

        Assert.Null(user.DeletedAt);
    }

    // ─── USR-11 / AD-023: both timestamps come from the supplied clock ───

    [Fact]
    public void Created_user_is_timestamped_from_the_supplied_time()
    {
        var user = Create().AsT0;

        Assert.Equal(Now, user.CreatedAt);
        Assert.Equal(Now, user.UpdatedAt);
    }

    [Fact]
    public void Creation_time_is_never_read_from_the_system_clock()
    {
        var backdated = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var user = User
            .Create("TICKET-2", "Ada Lovelace", "004215", Fingerprint, PictureContent, backdated)
            .AsT0;

        Assert.Equal(backdated, user.CreatedAt);
        Assert.Equal(backdated, user.UpdatedAt);
    }

    // ─── USR-03: the spectator's name ───

    [Fact]
    public void User_without_a_name_is_invalid()
    {
        var result = Create(name: null);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameRequired, result.AsT1.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void User_whose_name_is_only_whitespace_is_rejected_as_blank(string name)
    {
        var result = Create(name: name);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Surrounding_whitespace_is_trimmed_from_the_stored_name()
    {
        var user = Create(name: "  Ada Lovelace  ").AsT0;

        Assert.Equal("Ada Lovelace", user.Name);
    }

    [Fact]
    public void Name_is_trimmed_before_its_length_is_checked()
    {
        var paddedFullLengthName = "  " + new string('A', 100) + "  ";

        var result = Create(name: paddedFullLengthName);

        Assert.True(result.IsT0);
        Assert.Equal(new string('A', 100), result.AsT0.Name);
    }

    [Fact]
    public void User_with_a_name_longer_than_the_permitted_length_is_invalid()
    {
        var result = Create(name: new string('A', 101));

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameTooLong, result.AsT1.Message);
    }

    // ─── USR-02 / USR-04: value-object failures surface as their own errors ───

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void User_without_an_external_ref_is_invalid(string? externalRef)
    {
        var result = Create(externalRef: externalRef);

        Assert.True(result.IsT1);
        Assert.Equal("externalRef", result.AsT1.Field);
        Assert.Equal(ExternalRef.Errors.Required, result.AsT1.Message);
    }

    [Fact]
    public void User_with_an_over_long_external_ref_is_invalid()
    {
        var result = Create(externalRef: new string('T', 256));

        Assert.True(result.IsT1);
        Assert.Equal("externalRef", result.AsT1.Field);
        Assert.Equal(ExternalRef.Errors.TooLong, result.AsT1.Message);
    }

    [Fact]
    public void User_without_an_access_code_is_invalid()
    {
        var result = Create(accessCode: null);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
        Assert.Equal(AccessCode.Errors.Required, result.AsT1.Message);
    }

    [Theory]
    [InlineData("٤٥٦٧")]
    [InlineData("12a4")]
    [InlineData("123")]
    public void User_with_an_unusable_access_code_is_invalid(string accessCode)
    {
        var result = Create(accessCode: accessCode);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
    }
}
