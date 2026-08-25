using System.Reflection;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

public class AccessCodeTests
{
    // ─── USR-04: required, digits only, 4–20 characters ───

    [Fact]
    public void Access_code_is_stored_exactly_as_supplied()
    {
        var result = AccessCode.Create("004215");

        Assert.True(result.IsT0);
        Assert.Equal("004215", result.AsT0.Value);
    }

    [Fact]
    public void Missing_access_code_is_rejected()
    {
        var result = AccessCode.Create(null);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
        Assert.Equal(AccessCode.Errors.Required, result.AsT1.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("    ")]
    public void Blank_access_code_is_rejected(string value)
    {
        var result = AccessCode.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
        Assert.Equal(AccessCode.Errors.Required, result.AsT1.Message);
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("12 34")]
    [InlineData("1234-5")]
    [InlineData("+1234")]
    public void Access_code_containing_a_non_digit_is_rejected(string value)
    {
        var result = AccessCode.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
        Assert.Equal(AccessCode.Errors.MustBeNumeric, result.AsT1.Message);
    }

    // ─── Edge case: "digits" means ASCII 0-9; the keypad has no other keys ───

    [Theory]
    [InlineData("٤٥٦٧")] // Arabic-Indic
    [InlineData("۴۵۶۷")] // Extended Arabic-Indic
    [InlineData("１２３４")] // Fullwidth
    public void Access_code_with_non_ascii_digits_is_rejected(string value)
    {
        var result = AccessCode.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
        Assert.Equal(AccessCode.Errors.MustBeNumeric, result.AsT1.Message);
    }

    [Theory]
    [InlineData("1234")]
    [InlineData("12345678901234567890")]
    public void Access_code_at_the_boundary_of_the_permitted_length_is_accepted(string value)
    {
        var result = AccessCode.Create(value);

        Assert.True(result.IsT0);
        Assert.Equal(value, result.AsT0.Value);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456789012345678901")]
    public void Access_code_outside_the_permitted_length_is_rejected(string value)
    {
        var result = AccessCode.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("accessCode", result.AsT1.Field);
        Assert.Equal(AccessCode.Errors.OutOfRange, result.AsT1.Message);
    }

    // ─── AD-009: rehydration from the database bypasses validation ───

    [Fact]
    public void Access_code_rehydrated_from_storage_keeps_a_value_creation_would_reject()
    {
        var rehydrated = (AccessCode)
            typeof(AccessCode)
                .GetMethod("FromPersistence", BindingFlags.NonPublic | BindingFlags.Static)!
                .Invoke(null, ["12"])!;

        Assert.Equal("12", rehydrated.Value);
        Assert.True(AccessCode.Create("12").IsT1);
    }
}
