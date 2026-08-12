using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Tests.Domain;

[Trait("Category", "Unit")]
public class ValueObjectTests
{
    // ─── IP address (DEV-04, A-1, A-2) ────────────────────────────────────

    [Fact]
    public void Address_written_in_non_canonical_form_equals_its_canonical_form()
    {
        var nonCanonical = IpAddress.Create("192.168.001.001").AsT0;
        var canonical = IpAddress.Create("192.168.1.1").AsT0;

        Assert.Equal("192.168.1.1", nonCanonical.Value);
        Assert.Equal(canonical, nonCanonical);
    }

    [Fact]
    public void Address_is_stored_in_normalized_form()
    {
        var address = IpAddress.Create("2001:0db8:0000:0000:0000:0000:0000:0001").AsT0;

        Assert.Equal("2001:db8::1", address.Value);
    }

    [Fact]
    public void Version_six_address_is_accepted()
    {
        var result = IpAddress.Create("::1");

        Assert.True(result.IsT0);
        Assert.Equal("::1", result.AsT0.Value);
    }

    [Fact]
    public void Address_that_cannot_be_parsed_is_rejected()
    {
        var result = IpAddress.Create("not-an-ip");

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal(IpAddress.Errors.InvalidFormat, result.AsT1.Message);
    }

    [Fact]
    public void Missing_address_is_rejected()
    {
        var result = IpAddress.Create(null);

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal(IpAddress.Errors.Required, result.AsT1.Message);
    }

    [Fact]
    public void Blank_address_is_rejected()
    {
        var result = IpAddress.Create("   ");

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal(IpAddress.Errors.Required, result.AsT1.Message);
    }

    // ─── Port (DEV-04 + boundary edge cases) ──────────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(65535)]
    public void Port_at_the_boundary_of_the_valid_range_is_accepted(int value)
    {
        var result = Port.Create(value);

        Assert.True(result.IsT0);
        Assert.Equal(value, result.AsT0.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Port_outside_the_valid_range_is_rejected(int value)
    {
        var result = Port.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("httpPort", result.AsT1.Field);
        Assert.Equal(Port.Errors.OutOfRange, result.AsT1.Message);
    }

    [Fact]
    public void Missing_port_is_rejected()
    {
        var result = Port.Create(null);

        Assert.True(result.IsT1);
        Assert.Equal("httpPort", result.AsT1.Field);
        Assert.Equal(Port.Errors.Required, result.AsT1.Message);
    }

    // ─── Face capacity (DEV-04, A-4 + edge cases) ─────────────────────────

    [Theory]
    [InlineData(1)]
    [InlineData(1_000_000)]
    public void Face_capacity_at_the_boundary_of_the_valid_range_is_accepted(int value)
    {
        var result = FaceCapacity.Create(value);

        Assert.True(result.IsT0);
        Assert.Equal(value, result.AsT0.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Face_capacity_outside_the_valid_range_is_rejected(int value)
    {
        var result = FaceCapacity.Create(value);

        Assert.True(result.IsT1);
        Assert.Equal("faceCapacity", result.AsT1.Field);
        Assert.Equal(FaceCapacity.Errors.OutOfRange, result.AsT1.Message);
    }

    [Fact]
    public void Missing_face_capacity_is_rejected()
    {
        var result = FaceCapacity.Create(null);

        Assert.True(result.IsT1);
        Assert.Equal("faceCapacity", result.AsT1.Field);
        Assert.Equal(FaceCapacity.Errors.Required, result.AsT1.Message);
    }

    // ─── Equality (the unique-index key depends on it) ────────────────────

    [Fact]
    public void Two_different_addresses_are_not_equal()
    {
        var first = IpAddress.Create("192.168.1.1").AsT0;
        var second = IpAddress.Create("192.168.1.2").AsT0;

        Assert.NotEqual(first, second);
    }
}
