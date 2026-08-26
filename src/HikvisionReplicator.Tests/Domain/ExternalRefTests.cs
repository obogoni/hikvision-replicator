using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class ExternalRefTests
{
    // ─── USR-02: the integrator's key is non-blank and at most 255 characters ───

    [Fact]
    public void External_ref_is_stored_exactly_as_supplied()
    {
        var result = ExternalRef.Create("Ticket/2026%A1");

        Assert.True(result.IsT0);
        Assert.Equal("Ticket/2026%A1", result.AsT0.Value);
    }

    [Fact]
    public void Missing_external_ref_is_rejected()
    {
        var result = ExternalRef.Create(null);

        Assert.True(result.IsT1);
        Assert.Equal("externalRef", result.AsT1.Field);
        Assert.Equal(ExternalRef.Errors.Required, result.AsT1.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t")]
    public void Blank_external_ref_is_rejected(string value)
    {
        var result = ExternalRef.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("externalRef", result.AsT1.Field);
        Assert.Equal(ExternalRef.Errors.Required, result.AsT1.Message);
    }

    [Fact]
    public void External_ref_of_the_greatest_permitted_length_is_accepted()
    {
        var result = ExternalRef.Create(new string('T', 255));

        Assert.True(result.IsT0);
        Assert.Equal(255, result.AsT0.Value.Length);
    }

    [Fact]
    public void External_ref_longer_than_the_permitted_length_is_rejected()
    {
        var result = ExternalRef.Create(new string('T', 256));

        Assert.True(result.IsT1);
        Assert.Equal("externalRef", result.AsT1.Field);
        Assert.Equal(ExternalRef.Errors.TooLong, result.AsT1.Message);
    }

    // ─── A-15: two refs differing only by case are two distinct spectators ───

    [Fact]
    public void Two_refs_differing_only_by_letter_case_are_not_equal()
    {
        var lower = ExternalRef.Create("ticket-1").AsT0;
        var upper = ExternalRef.Create("TICKET-1").AsT0;

        Assert.NotEqual(lower, upper);
    }

    [Fact]
    public void Two_refs_spelled_identically_are_equal()
    {
        var first = ExternalRef.Create("TICKET-1").AsT0;
        var second = ExternalRef.Create("TICKET-1").AsT0;

        Assert.Equal(first, second);
    }

    // ─── AD-009: rehydration from the database bypasses validation ───

    [Fact]
    public void Ref_rehydrated_from_storage_keeps_a_value_creation_would_reject()
    {
        var rehydrated = ExternalRef.FromPersistence(new string('T', 300));

        Assert.Equal(new string('T', 300), rehydrated.Value);
        Assert.True(ExternalRef.Create(new string('T', 300)).IsT1);
    }

}
