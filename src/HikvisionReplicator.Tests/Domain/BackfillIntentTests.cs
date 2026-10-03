using System.Reflection;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class BackfillIntentTests
{
    private static readonly DateTime RegisteredOn = new(2026, 10, 3, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpandedOn = new(2026, 10, 3, 18, 5, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 3, 19, 0, 0, DateTimeKind.Utc);

    private static BackfillIntent Owed() => BackfillIntent.Create(42, RegisteredOn);

    // ─── A newly registered reader is owed the roster and has not been given it ───

    [Fact]
    public void A_newly_registered_reader_is_owed_a_backfill_it_has_not_had()
    {
        var intent = Owed();

        Assert.Equal(BackfillStatus.Pending, intent.Status);
        Assert.Null(intent.ExpandedAt);
    }

    [Fact]
    public void A_backfill_intent_records_the_reader_it_is_owed_to()
    {
        Assert.Equal(42, Owed().DeviceId);
    }

    [Fact]
    public void A_backfill_intent_is_timestamped_from_the_supplied_clock()
    {
        var intent = Owed();

        Assert.Equal(RegisteredOn, intent.CreatedAt);
        Assert.Equal(RegisteredOn, intent.UpdatedAt);
    }

    // ─── REP-20: expansion is terminal ───

    [Fact]
    public void An_expanded_backfill_records_when_the_roster_was_staged()
    {
        var intent = Owed();

        var result = intent.MarkExpanded(ExpandedOn);

        Assert.True(result.IsT0);
        Assert.Equal(BackfillStatus.Expanded, intent.Status);
        Assert.Equal(ExpandedOn, intent.ExpandedAt);
        Assert.Equal(ExpandedOn, intent.UpdatedAt);
    }

    [Fact]
    public void A_backfill_can_only_be_expanded_once()
    {
        var intent = Owed();
        intent.MarkExpanded(ExpandedOn);

        var result = intent.MarkExpanded(Later);

        Assert.True(result.IsT1);
        Assert.Equal(BackfillIntent.Errors.StatusField, result.AsT1.Field);
        Assert.Equal(BackfillIntent.Errors.AlreadyExpanded, result.AsT1.Message);
        Assert.Equal(ExpandedOn, intent.ExpandedAt);
        Assert.Equal(ExpandedOn, intent.UpdatedAt);
    }

    // ─── AD-005: the factory is the only way in ───

    [Fact]
    public void A_backfill_intent_cannot_be_constructed_around_the_factory()
    {
        var reachable = typeof(BackfillIntent).GetConstructors(
            BindingFlags.Instance | BindingFlags.Public
        );

        Assert.Empty(reachable);
    }
}
