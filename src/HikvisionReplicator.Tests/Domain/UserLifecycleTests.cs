using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class UserLifecycleTests
{
    private static readonly DateTime RegisteredOn = new(2026, 8, 25, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime RemovedOn = new(2026, 8, 25, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ResoldOn = new(2026, 8, 25, 20, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] StoredContent = [0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22];
    private static readonly byte[] NewContent = [0xFF, 0xD8, 0xFF, 0xE1, 0x33, 0x44];

    private static readonly FaceFingerprint StoredFace = FaceFingerprint
        .Create("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", 81_920, 720, 960)
        .AsT0;

    private static readonly FaceFingerprint NewFace = FaceFingerprint
        .Create("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae", 61_440, 640, 480)
        .AsT0;

    private static User Registered() =>
        User.Create("TICKET-1", "Ada Lovelace", "004215", StoredFace, StoredContent, RegisteredOn)
            .AsT0;

    private static User Tombstoned()
    {
        var user = Registered();
        user.MarkDeleted(RemovedOn);
        return user;
    }

    // ─── USR-29: the row survives, marked deleted at the supplied time ───

    [Fact]
    public void Removed_user_is_tombstoned_at_the_supplied_time()
    {
        var user = Registered();

        user.MarkDeleted(RemovedOn);

        Assert.Equal(RemovedOn, user.DeletedAt);
    }

    // ─── USR-30: the biometric is destroyed, the identity fields survive ───

    [Fact]
    public void Removing_a_user_destroys_its_face_picture()
    {
        var user = Registered();

        user.MarkDeleted(RemovedOn);

        Assert.Null(user.Picture);
    }

    [Fact]
    public void Removed_user_keeps_the_identity_fields_a_device_removal_needs()
    {
        var user = Registered();

        user.MarkDeleted(RemovedOn);

        Assert.Equal("TICKET-1", user.ExternalRef.Value);
        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Equal("004215", user.AccessCode.Value);
    }

    // ─── A-16: removing an already-removed user changes nothing ───

    [Fact]
    public void Removing_an_already_removed_user_leaves_its_tombstone_untouched()
    {
        var user = Tombstoned();

        user.MarkDeleted(ResoldOn);

        Assert.Equal(RemovedOn, user.DeletedAt);
        Assert.Equal(RemovedOn, user.UpdatedAt);
    }

    // ─── USR-34: a resurrection clears the tombstone and rewrites the record ───

    [Fact]
    public void Resurrected_user_is_active_again()
    {
        var user = Tombstoned();

        var result = user.Restore("Ada King", "778899", NewFace, NewContent, ResoldOn);

        Assert.True(result.IsT0);
        Assert.Null(user.DeletedAt);
    }

    [Fact]
    public void Resurrected_user_takes_the_newly_supplied_details()
    {
        var user = Tombstoned();

        user.Restore("  Ada King  ", "778899", NewFace, NewContent, ResoldOn);

        Assert.Equal("Ada King", user.Name);
        Assert.Equal("778899", user.AccessCode.Value);
        Assert.Equal("TICKET-1", user.ExternalRef.Value);
    }

    [Fact]
    public void Resurrected_user_carries_the_newly_supplied_face_picture()
    {
        var user = Tombstoned();

        user.Restore("Ada King", "778899", NewFace, NewContent, ResoldOn);

        Assert.Equal(NewFace, user.Face);
        Assert.NotNull(user.Picture);
        Assert.Equal(NewContent, user.Picture.Content);
    }

    [Fact]
    public void Resurrected_user_is_timestamped_from_the_supplied_time()
    {
        var user = Tombstoned();

        user.Restore("Ada King", "778899", NewFace, NewContent, ResoldOn);

        Assert.Equal(ResoldOn, user.UpdatedAt);
        Assert.Equal(RegisteredOn, user.CreatedAt);
    }

    // ─── USR-34 / A-7: a resurrection is a registration for validation purposes ───

    [Fact]
    public void Resurrection_without_a_face_picture_is_rejected()
    {
        var user = Tombstoned();

        var result = user.Restore("Ada King", "778899", null!, null!, ResoldOn);

        Assert.True(result.IsT1);
        Assert.Equal("facePicture", result.AsT1.Field);
        Assert.Equal(User.Errors.PictureRequired, result.AsT1.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Resurrection_without_a_usable_name_is_rejected(string? name)
    {
        var user = Tombstoned();

        var result = user.Restore(name, "778899", NewFace, NewContent, ResoldOn);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Resurrection_with_a_name_longer_than_the_permitted_length_is_rejected()
    {
        var user = Tombstoned();

        var result = user.Restore(new string('A', 101), "778899", NewFace, NewContent, ResoldOn);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(User.Errors.NameTooLong, result.AsT1.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("٤٥٦٧")]
    [InlineData("123")]
    public void Resurrection_without_a_usable_access_code_is_rejected(string? accessCode)
    {
        var user = Tombstoned();

        var result = user.Restore("Ada King", accessCode, NewFace, NewContent, ResoldOn);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
    }

    [Fact]
    public void Rejected_resurrection_leaves_the_user_tombstoned_and_faceless()
    {
        var user = Tombstoned();

        var result = user.Restore("Ada King", "not-a-code", NewFace, NewContent, ResoldOn);

        Assert.True(result.IsT1);
        Assert.Equal(RemovedOn, user.DeletedAt);
        Assert.Null(user.Picture);
        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Equal(StoredFace, user.Face);
        Assert.Equal(RemovedOn, user.UpdatedAt);
    }

    // ─── Resurrection is not an update path ───

    [Fact]
    public void Resurrecting_a_user_that_was_never_removed_is_rejected()
    {
        var user = Registered();

        var result = user.Restore("Ada King", "778899", NewFace, NewContent, ResoldOn);

        Assert.True(result.IsT1);
        Assert.Equal("externalRef", result.AsT1.Field);
        Assert.Equal(User.Errors.AlreadyActive, result.AsT1.Message);
        Assert.Equal("Ada Lovelace", user.Name);
        Assert.Equal(StoredFace, user.Face);
        Assert.Equal(RegisteredOn, user.UpdatedAt);
    }
}
