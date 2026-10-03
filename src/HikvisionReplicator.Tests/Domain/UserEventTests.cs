using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Events;

namespace HikvisionReplicator.Tests.Domain;

/// <summary>
/// What the spectator aggregate says happened. The fan-out reads nothing else, so a raise
/// in the wrong branch queues the wrong work — or none at all.
/// </summary>
public class UserEventTests
{
    private static readonly DateTime RegisteredOn = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 3, 19, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime LaterStill = new(2026, 10, 3, 20, 0, 0, DateTimeKind.Utc);

    private static readonly byte[] StoredContent = [0xFF, 0xD8, 0xFF, 0xE0, 0x11, 0x22];
    private static readonly byte[] NewContent = [0xFF, 0xD8, 0xFF, 0xE1, 0x33, 0x44];

    private static readonly FaceFingerprint StoredFace = FaceFingerprint
        .Create("9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08", 81_920, 720, 960)
        .AsT0;

    private static readonly FaceFingerprint NewFace = FaceFingerprint
        .Create("2c26b46b68ffc68ff99b453c1d30413413422d706483bfa0f98a5e886266e7ae", 61_440, 640, 480)
        .AsT0;

    private static User Created() =>
        User.Create("TICKET-1", "Ada Lovelace", "004215", StoredFace, StoredContent, RegisteredOn)
            .AsT0;

    /// <summary>A registered spectator, as a save leaves it: stored, and nothing owed.</summary>
    private static User Registered()
    {
        var user = Created();
        user.ClearDomainEvents();
        return user;
    }

    private static User Tombstoned()
    {
        var user = Registered();
        user.MarkDeleted(Later);
        user.ClearDomainEvents();
        return user;
    }

    // ─── REP-01: registration is announced ───

    [Fact]
    public void Registering_a_spectator_announces_a_registration()
    {
        var user = Created();

        var raised = Assert.Single(user.DomainEvents);
        var registered = Assert.IsType<UserRegistered>(raised);
        Assert.Equal(RegisteredOn, registered.OccurredAt);
    }

    // ─── REP-02: a correction that lands is announced as a change ───

    [Fact]
    public void Correcting_a_spectator_announces_a_change()
    {
        var user = Registered();

        user.Update("Ada King", "004215", null, null, Later);

        var raised = Assert.Single(user.DomainEvents);
        var changed = Assert.IsType<UserChanged>(raised);
        Assert.Equal(Later, changed.OccurredAt);
    }

    [Fact]
    public void Replacing_only_the_face_picture_announces_a_change()
    {
        var user = Registered();

        user.Update("Ada Lovelace", "004215", NewFace, NewContent, Later);

        Assert.IsType<UserChanged>(Assert.Single(user.DomainEvents));
    }

    // ─── REP-04 / USR-26: an upsert that changes nothing announces nothing ───

    [Fact]
    public void An_upsert_that_changes_nothing_announces_nothing()
    {
        var user = Registered();

        var result = user.Update("Ada Lovelace", "004215", null, null, Later);

        Assert.True(result.IsT0);
        Assert.Empty(user.DomainEvents);
        Assert.Equal(RegisteredOn, user.UpdatedAt);
    }

    [Fact]
    public void A_rejected_correction_announces_nothing()
    {
        var user = Registered();

        var result = user.Update(null, "004215", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Empty(user.DomainEvents);
    }

    // ─── REP-06: a resurrection is a return, not a correction ───

    [Fact]
    public void Resurrecting_a_spectator_announces_a_return_and_not_a_change()
    {
        var user = Tombstoned();

        user.Restore("Ada King", "778899", NewFace, NewContent, LaterStill);

        var raised = Assert.Single(user.DomainEvents);
        var restored = Assert.IsType<UserRestored>(raised);
        Assert.Equal(LaterStill, restored.OccurredAt);
    }

    [Fact]
    public void A_rejected_resurrection_announces_nothing()
    {
        var user = Tombstoned();

        var result = user.Restore("Ada King", "not-a-code", NewFace, NewContent, LaterStill);

        Assert.True(result.IsT1);
        Assert.Empty(user.DomainEvents);
    }

    // ─── REP-03 / REP-42: a removal is announced once, and only once ───

    [Fact]
    public void Removing_a_spectator_announces_a_removal()
    {
        var user = Registered();

        user.MarkDeleted(Later);

        var raised = Assert.Single(user.DomainEvents);
        var removed = Assert.IsType<UserRemoved>(raised);
        Assert.Equal(Later, removed.OccurredAt);
    }

    [Fact]
    public void Removing_an_already_removed_spectator_announces_nothing()
    {
        var user = Tombstoned();

        user.MarkDeleted(LaterStill);

        Assert.Empty(user.DomainEvents);
    }
}
