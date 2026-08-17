using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Tests.Domain;

[Trait("Category", "Unit")]
public class DeviceCreateTests
{
    private static readonly DateTime Now = new(2026, 8, 12, 10, 30, 0, DateTimeKind.Utc);

    private static OneOf.OneOf<Device, Api.Shared.ValidationError> Create(
        string? name = "Front Gate Reader",
        string? ipAddress = "192.168.1.10",
        int? httpPort = 80,
        string? username = "admin",
        string encryptedPassword = "IV:cipher",
        int? faceCapacity = 10_000
    ) => Device.Create(name, ipAddress, httpPort, username, encryptedPassword, faceCapacity, Now);

    // ─── DEV-01: a valid device is constructed from the supplied values ───

    [Fact]
    public void Device_is_created_from_the_supplied_values()
    {
        var result = Create();

        Assert.True(result.IsT0);
        var device = result.AsT0;
        Assert.Equal("Front Gate Reader", device.Name);
        Assert.Equal("192.168.1.10", device.IpAddress.Value);
        Assert.Equal(80, device.HttpPort.Value);
        Assert.Equal("admin", device.Username);
        Assert.Equal("IV:cipher", device.EncryptedPassword);
        Assert.Equal(10_000, device.FaceCapacity.Value);
    }

    [Fact]
    public void Created_device_is_timestamped_from_the_supplied_time()
    {
        var device = Create().AsT0;

        Assert.Equal(Now, device.CreatedAt);
        Assert.Equal(Now, device.UpdatedAt);
    }

    [Fact]
    public void Created_device_stores_its_address_in_normalized_form()
    {
        var device = Create(ipAddress: "192.168.001.001").AsT0;

        Assert.Equal("192.168.1.1", device.IpAddress.Value);
    }

    // ─── DEV-02: missing or blank required fields ─────────────────────────

    [Fact]
    public void Device_without_a_name_is_invalid()
    {
        var result = Create(name: null);

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(Device.Errors.NameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Device_with_a_blank_name_is_invalid()
    {
        var result = Create(name: "   ");

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(Device.Errors.NameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Device_without_an_address_is_invalid()
    {
        var result = Create(ipAddress: null);

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal(IpAddress.Errors.Required, result.AsT1.Message);
    }

    [Fact]
    public void Device_without_a_port_is_invalid()
    {
        var result = Create(httpPort: null);

        Assert.True(result.IsT1);
        Assert.Equal("httpPort", result.AsT1.Field);
        Assert.Equal(Port.Errors.Required, result.AsT1.Message);
    }

    [Fact]
    public void Device_without_a_username_is_invalid()
    {
        var result = Create(username: null);

        Assert.True(result.IsT1);
        Assert.Equal("username", result.AsT1.Field);
        Assert.Equal(Device.Errors.UsernameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Device_with_a_blank_username_is_invalid()
    {
        var result = Create(username: " ");

        Assert.True(result.IsT1);
        Assert.Equal("username", result.AsT1.Field);
        Assert.Equal(Device.Errors.UsernameRequired, result.AsT1.Message);
    }

    [Fact]
    public void Device_without_a_face_capacity_is_invalid()
    {
        var result = Create(faceCapacity: null);

        Assert.True(result.IsT1);
        Assert.Equal("faceCapacity", result.AsT1.Field);
        Assert.Equal(FaceCapacity.Errors.Required, result.AsT1.Message);
    }

    // ─── DEV-03: length boundaries on name and username ───────────────────

    [Fact]
    public void Device_with_a_name_of_the_maximum_length_is_valid()
    {
        var result = Create(name: new string('n', 100));

        Assert.True(result.IsT0);
        Assert.Equal(100, result.AsT0.Name.Length);
    }

    [Fact]
    public void Device_with_a_name_beyond_the_maximum_length_is_invalid()
    {
        var result = Create(name: new string('n', 101));

        Assert.True(result.IsT1);
        Assert.Equal("name", result.AsT1.Field);
        Assert.Equal(Device.Errors.NameTooLong, result.AsT1.Message);
    }

    [Fact]
    public void Device_with_a_username_of_the_maximum_length_is_valid()
    {
        var result = Create(username: new string('u', 100));

        Assert.True(result.IsT0);
        Assert.Equal(100, result.AsT0.Username.Length);
    }

    [Fact]
    public void Device_with_a_username_beyond_the_maximum_length_is_invalid()
    {
        var result = Create(username: new string('u', 101));

        Assert.True(result.IsT1);
        Assert.Equal("username", result.AsT1.Field);
        Assert.Equal(Device.Errors.UsernameTooLong, result.AsT1.Message);
    }

    // ─── DEV-04: out-of-range values ──────────────────────────────────────

    [Fact]
    public void Device_with_an_unparseable_address_is_invalid()
    {
        var result = Create(ipAddress: "not-an-ip");

        Assert.True(result.IsT1);
        Assert.Equal("ipAddress", result.AsT1.Field);
        Assert.Equal(IpAddress.Errors.InvalidFormat, result.AsT1.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Device_with_a_port_outside_the_valid_range_is_invalid(int port)
    {
        var result = Create(httpPort: port);

        Assert.True(result.IsT1);
        Assert.Equal("httpPort", result.AsT1.Field);
        Assert.Equal(Port.Errors.OutOfRange, result.AsT1.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1_000_001)]
    public void Device_with_a_face_capacity_outside_the_valid_range_is_invalid(int capacity)
    {
        var result = Create(faceCapacity: capacity);

        Assert.True(result.IsT1);
        Assert.Equal("faceCapacity", result.AsT1.Field);
        Assert.Equal(FaceCapacity.Errors.OutOfRange, result.AsT1.Message);
    }
}
