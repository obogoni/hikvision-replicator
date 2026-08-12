using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public class Device : IAggregateRoot
{
    public const int MaxNameLength = 100;
    public const int MaxUsernameLength = 100;

    public int Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IpAddress IpAddress { get; private set; } = null!;
    public Port HttpPort { get; private set; } = null!;
    public string Username { get; private set; } = string.Empty;
    public string EncryptedPassword { get; private set; } = string.Empty;
    public FaceCapacity FaceCapacity { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private Device() { } // for EF Core

    private Device(
        string name,
        IpAddress ipAddress,
        Port httpPort,
        string username,
        string encryptedPassword,
        FaceCapacity faceCapacity,
        DateTime now
    )
    {
        Name = name;
        IpAddress = ipAddress;
        HttpPort = httpPort;
        Username = username;
        EncryptedPassword = encryptedPassword;
        FaceCapacity = faceCapacity;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static OneOf<Device, ValidationError> Create(
        string? name,
        string? ipAddress,
        int? httpPort,
        string? username,
        string encryptedPassword,
        int? faceCapacity,
        DateTime now
    )
    {
        if (string.IsNullOrWhiteSpace(name))
            return new ValidationError(Errors.NameField, Errors.NameRequired);
        if (name.Length > MaxNameLength)
            return new ValidationError(Errors.NameField, Errors.NameTooLong);

        var ipResult = IpAddress.Create(ipAddress);
        if (ipResult.TryPickT1(out var ipError, out var ip))
            return ipError;

        var portResult = Port.Create(httpPort);
        if (portResult.TryPickT1(out var portError, out var port))
            return portError;

        if (string.IsNullOrWhiteSpace(username))
            return new ValidationError(Errors.UsernameField, Errors.UsernameRequired);
        if (username.Length > MaxUsernameLength)
            return new ValidationError(Errors.UsernameField, Errors.UsernameTooLong);

        var capacityResult = FaceCapacity.Create(faceCapacity);
        if (capacityResult.TryPickT1(out var capacityError, out var capacity))
            return capacityError;

        return new Device(name, ip, port, username, encryptedPassword, capacity, now);
    }

    /// <summary>
    /// Applies the supplied fields; a null field means "leave unchanged" (DEV-18).
    /// Every field is validated before any is assigned, so a rejected update leaves
    /// the aggregate untouched (DEV-19). <see cref="UpdatedAt"/> advances only when a
    /// value actually differs from the current one (DEV-23).
    /// </summary>
    public OneOf<Success, ValidationError> Update(
        string? name,
        string? ipAddress,
        int? httpPort,
        string? username,
        string? encryptedPassword,
        int? faceCapacity,
        DateTime now
    )
    {
        // ── Validate everything first — no assignment happens above this line ──
        if (name is not null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return new ValidationError(Errors.NameField, Errors.NameEmpty);
            if (name.Length > MaxNameLength)
                return new ValidationError(Errors.NameField, Errors.NameTooLong);
        }

        IpAddress? newIpAddress = null;
        if (ipAddress is not null)
        {
            var ipResult = IpAddress.Create(ipAddress);
            if (ipResult.TryPickT1(out var ipError, out var ip))
                return ipError;
            newIpAddress = ip;
        }

        Port? newHttpPort = null;
        if (httpPort is not null)
        {
            var portResult = Port.Create(httpPort);
            if (portResult.TryPickT1(out var portError, out var port))
                return portError;
            newHttpPort = port;
        }

        if (username is not null)
        {
            if (string.IsNullOrWhiteSpace(username))
                return new ValidationError(Errors.UsernameField, Errors.UsernameEmpty);
            if (username.Length > MaxUsernameLength)
                return new ValidationError(Errors.UsernameField, Errors.UsernameTooLong);
        }

        FaceCapacity? newFaceCapacity = null;
        if (faceCapacity is not null)
        {
            var capacityResult = FaceCapacity.Create(faceCapacity);
            if (capacityResult.TryPickT1(out var capacityError, out var capacity))
                return capacityError;
            newFaceCapacity = capacity;
        }

        // ── Everything is valid: apply only what actually differs ──
        var changed = false;

        if (name is not null && name != Name)
        {
            Name = name;
            changed = true;
        }

        if (newIpAddress is not null && newIpAddress != IpAddress)
        {
            IpAddress = newIpAddress;
            changed = true;
        }

        if (newHttpPort is not null && newHttpPort != HttpPort)
        {
            HttpPort = newHttpPort;
            changed = true;
        }

        if (username is not null && username != Username)
        {
            Username = username;
            changed = true;
        }

        if (encryptedPassword is not null && encryptedPassword != EncryptedPassword)
        {
            EncryptedPassword = encryptedPassword;
            changed = true;
        }

        if (newFaceCapacity is not null && newFaceCapacity != FaceCapacity)
        {
            FaceCapacity = newFaceCapacity;
            changed = true;
        }

        if (changed)
            UpdatedAt = now;

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
