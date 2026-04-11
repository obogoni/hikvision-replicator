using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public class Device : IAggregateRoot
{
    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IpAddress IpAddress { get; private set; } = null!;
    public Port HttpPort { get; private set; } = null!;
    public string Username { get; private set; } = string.Empty;
    public string EncryptedPassword { get; private set; } = string.Empty;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Device() { } // for EF Core

    private Device(
        string name,
        IpAddress ipAddress,
        Port httpPort,
        string username,
        string encryptedPassword,
        DateTime now)
    {
        Name = name;
        IpAddress = ipAddress;
        HttpPort = httpPort;
        Username = username;
        EncryptedPassword = encryptedPassword;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static OneOf<Device, ValidationError> Create(
        string? name,
        string? ipAddress,
        int? httpPort,
        string? username,
        string encryptedPassword,
        DateTime now)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new ValidationError(Errors.NameField, Errors.NameRequired);
        if (name.Length > 100)
            return new ValidationError(Errors.NameField, Errors.NameTooLong);

        var ipResult = IpAddress.Create(ipAddress);
        if (ipResult.TryPickT1(out var ipErr, out var ip))
            return ipErr;

        var portResult = Port.Create(httpPort);
        if (portResult.TryPickT1(out var portErr, out var port))
            return portErr;

        if (string.IsNullOrWhiteSpace(username))
            return new ValidationError(Errors.UsernameField, Errors.UsernameRequired);
        if (username.Length > 100)
            return new ValidationError(Errors.UsernameField, Errors.UsernameTooLong);

        return new Device(name, ip, port, username, encryptedPassword, now);
    }

    public OneOf<Success, ValidationError> Update(
        string? name,
        string? ipAddress,
        int? httpPort,
        string? username,
        string? encryptedPassword)
    {
        if (name is not null)
        {
            if (name.Length == 0)
                return new ValidationError(Errors.NameField, Errors.NameEmpty);
            if (name.Length > 100)
                return new ValidationError(Errors.NameField, Errors.NameTooLong);
        }

        IpAddress? newIp = null;
        if (ipAddress is not null)
        {
            var ipResult = IpAddress.Create(ipAddress);
            if (ipResult.TryPickT1(out var ipErr, out var ip))
                return ipErr;
            newIp = ip;
        }

        Port? newPort = null;
        if (httpPort is not null)
        {
            var portResult = Port.Create(httpPort);
            if (portResult.TryPickT1(out var portErr, out var port))
                return portErr;
            newPort = port;
        }

        if (username is not null)
        {
            if (username.Length == 0)
                return new ValidationError(Errors.UsernameField, Errors.UsernameEmpty);
            if (username.Length > 100)
                return new ValidationError(Errors.UsernameField, Errors.UsernameTooLong);
        }

        if (name is not null) Name = name;
        if (newIp is not null) IpAddress = newIp;
        if (newPort is not null) HttpPort = newPort;
        if (username is not null) Username = username;
        if (encryptedPassword is not null) EncryptedPassword = encryptedPassword;
        UpdatedAt = DateTime.UtcNow;

        return new Success();
    }

    public static class Errors
    {
        public const string NameField = "name";
        public const string NameRequired = "Name is required.";
        public const string NameEmpty = "Name cannot be empty.";
        public const string NameTooLong = "Name must be 100 characters or fewer.";

        public const string UsernameField = "username";
        public const string UsernameRequired = "Username is required.";
        public const string UsernameEmpty = "Username cannot be empty.";
        public const string UsernameTooLong = "Username must be 100 characters or fewer.";
    }
}
