using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

[Trait("Category", "Unit")]
public class DeviceUpdateTests
{
    private static readonly DateTime CreatedOn = new(2026, 8, 12, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 8, 12, 11, 0, 0, DateTimeKind.Utc);

    private static Device Registered() =>
        Device
            .Create("Front Gate Reader", "192.168.1.10", 80, "admin", "IV:cipher", 10_000, CreatedOn)
            .AsT0;

    // ─── DEV-18: only the supplied fields are applied ─────────────────────

    [Fact]
    public void Updating_only_the_name_leaves_every_other_value_unchanged()
    {
        var device = Registered();

        var result = device.Update("Back Gate Reader", null, null, null, null, null, Later);

        Assert.True(result.IsT0);
        Assert.Equal("Back Gate Reader", device.Name);
        Assert.Equal("192.168.1.10", device.IpAddress.Value);
        Assert.Equal(80, device.HttpPort.Value);
        Assert.Equal("admin", device.Username);
        Assert.Equal("IV:cipher", device.EncryptedPassword);
        Assert.Equal(10_000, device.FaceCapacity.Value);
    }

    [Fact]
    public void Update_supplying_no_fields_leaves_every_value_unchanged()
    {
        var device = Registered();

        var result = device.Update(null, null, null, null, null, null, Later);

        Assert.True(result.IsT0);
        Assert.Equal("Front Gate Reader", device.Name);
        Assert.Equal("192.168.1.10", device.IpAddress.Value);
        Assert.Equal(80, device.HttpPort.Value);
        Assert.Equal("admin", device.Username);
        Assert.Equal("IV:cipher", device.EncryptedPassword);
        Assert.Equal(10_000, device.FaceCapacity.Value);
    }

    [Fact]
    public void Every_supplied_field_is_applied()
    {
        var device = Registered();

        var result = device.Update(
            "Back Gate Reader",
            "10.0.0.5",
            8080,
            "operator",
            "IV2:cipher2",
            50_000,
            Later
        );

        Assert.True(result.IsT0);
        Assert.Equal("Back Gate Reader", device.Name);
        Assert.Equal("10.0.0.5", device.IpAddress.Value);
        Assert.Equal(8080, device.HttpPort.Value);
        Assert.Equal("operator", device.Username);
        Assert.Equal("IV2:cipher2", device.EncryptedPassword);
        Assert.Equal(50_000, device.FaceCapacity.Value);
    }

    // ─── DEV-23: the timestamp moves only on a real change ────────────────

    [Fact]
    public void Update_supplying_no_fields_leaves_the_update_time_unadvanced()
    {
        var device = Registered();

        device.Update(null, null, null, null, null, null, Later);

        Assert.Equal(CreatedOn, device.UpdatedAt);
    }

    [Fact]
    public void Update_repeating_the_current_values_leaves_the_update_time_unadvanced()
    {
        var device = Registered();

        device.Update("Front Gate Reader", "192.168.1.10", 80, "admin", "IV:cipher", 10_000, Later);

        Assert.Equal(CreatedOn, device.UpdatedAt);
    }

    [Fact]
    public void Address_repeated_in_non_canonical_form_is_not_treated_as_a_change()
    {
        var device = Device
            .Create("Front Gate Reader", "192.168.1.1", 80, "admin", "IV:cipher", 10_000, CreatedOn)
            .AsT0;

        device.Update(null, "192.168.001.001", null, null, null, null, Later);

        Assert.Equal("192.168.1.1", device.IpAddress.Value);
        Assert.Equal(CreatedOn, device.UpdatedAt);
    }

    [Fact]
    public void Update_that_changes_a_value_advances_the_update_time()
    {
        var device = Registered();

        device.Update("Back Gate Reader", null, null, null, null, null, Later);

        Assert.Equal(Later, device.UpdatedAt);
    }

    [Fact]
    public void Update_that_changes_a_value_leaves_the_registration_time_untouched()
    {
        var device = Registered();

        device.Update("Back Gate Reader", null, null, null, null, null, Later);

        Assert.Equal(CreatedOn, device.CreatedAt);
    }

    [Fact]
    public void Replacing_the_stored_credential_advances_the_update_time()
    {
        var device = Registered();

        device.Update(null, null, null, null, "IV2:cipher2", null, Later);

        Assert.Equal("IV2:cipher2", device.EncryptedPassword);
        Assert.Equal(Later, device.UpdatedAt);
    }

    // Every field gates the update time independently, so proving one field
    // says nothing about the other five. Both halves of each guard are
    // covered: a real change must advance, an identical value must not.

    [Theory]
    [InlineData("name")]
    [InlineData("ipAddress")]
    [InlineData("httpPort")]
    [InlineData("username")]
    [InlineData("password")]
    [InlineData("faceCapacity")]
    public void Changing_any_single_field_on_its_own_advances_the_update_time(string field)
    {
        var device = Registered();

        var result = device.Update(
            field == "name" ? "Back Gate Reader" : null,
            field == "ipAddress" ? "192.168.1.99" : null,
            field == "httpPort" ? 8080 : null,
            field == "username" ? "operator" : null,
            field == "password" ? "IV2:cipher2" : null,
            field == "faceCapacity" ? 50_000 : null,
            Later
        );

        Assert.True(result.IsT0);
        Assert.Equal(Later, device.UpdatedAt);
    }

    [Theory]
    [InlineData("name")]
    [InlineData("ipAddress")]
    [InlineData("httpPort")]
    [InlineData("username")]
    [InlineData("password")]
    [InlineData("faceCapacity")]
    public void Resupplying_a_field_with_its_current_value_leaves_the_update_time_alone(
        string field
    )
    {
        var device = Registered();

        var result = device.Update(
            field == "name" ? "Front Gate Reader" : null,
            field == "ipAddress" ? "192.168.1.10" : null,
            field == "httpPort" ? 80 : null,
            field == "username" ? "admin" : null,
            field == "password" ? "IV:cipher" : null,
            field == "faceCapacity" ? 10_000 : null,
            Later
        );

        Assert.True(result.IsT0);
        Assert.Equal(CreatedOn, device.UpdatedAt);
    }

    // ─── DEV-19: a rejected update persists no partial change ─────────────

    [Fact]
    public void Rejected_update_leaves_the_device_entirely_unchanged()
    {
        var device = Registered();

        // The name is valid and comes first; the address is not — nothing may be applied.
        var result = device.Update("Back Gate Reader", "not-an-ip", 8080, "operator", null, 5, Later);

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal("Front Gate Reader", device.Name);
        Assert.Equal("192.168.1.10", device.IpAddress.Value);
        Assert.Equal(80, device.HttpPort.Value);
        Assert.Equal("admin", device.Username);
        Assert.Equal("IV:cipher", device.EncryptedPassword);
        Assert.Equal(10_000, device.FaceCapacity.Value);
        Assert.Equal(CreatedOn, device.UpdatedAt);
    }

    [Fact]
    public void Update_with_an_unparseable_address_is_invalid()
    {
        var device = Registered();

        var result = device.Update(null, "not-an-ip", null, null, null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal(IpAddress.Errors.InvalidFormat, result.AsT1.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Update_with_a_port_outside_the_valid_range_is_invalid(int port)
    {
        var device = Registered();

        var result = device.Update(null, null, port, null, null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("httpPort", result.AsT1.Field);
        Assert.Equal(Port.Errors.OutOfRange, result.AsT1.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void Update_with_a_face_capacity_outside_the_valid_range_is_invalid(int capacity)
    {
        var device = Registered();

        var result = device.Update(null, null, null, null, null, capacity, Later);

        Assert.True(result.IsT1);
        Assert.Equal("faceCapacity", result.AsT1.Field);
        Assert.Equal(FaceCapacity.Errors.OutOfRange, result.AsT1.Message);
    }

    [Fact]
    public void Update_with_a_name_beyond_the_maximum_length_is_invalid()
    {
        var device = Registered();

        var result = device.Update(new string('n', 101), null, null, null, null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(Device.Errors.NameTooLong, result.AsT1.Message);
    }

    [Fact]
    public void Update_with_a_name_of_the_maximum_length_is_valid()
    {
        var device = Registered();

        var result = device.Update(new string('n', 100), null, null, null, null, null, Later);

        Assert.True(result.IsT0);
        Assert.Equal(100, device.Name.Length);
    }

    [Fact]
    public void Update_blanking_the_name_is_invalid()
    {
        var device = Registered();

        var result = device.Update("   ", null, null, null, null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(Device.Errors.NameEmpty, result.AsT1.Message);
    }

    [Fact]
    public void Update_with_a_username_beyond_the_maximum_length_is_invalid()
    {
        var device = Registered();

        var result = device.Update(null, null, null, new string('u', 101), null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("username", result.AsT1.Field);
        Assert.Equal(Device.Errors.UsernameTooLong, result.AsT1.Message);
    }

    [Fact]
    public void Update_blanking_the_username_is_invalid()
    {
        var device = Registered();

        var result = device.Update(null, null, null, " ", null, null, Later);

        Assert.True(result.IsT1);
        Assert.Equal("username", result.AsT1.Field);
        Assert.Equal(Device.Errors.UsernameEmpty, result.AsT1.Message);
    }
}
