using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

public class User : IAggregateRoot
{
    private const int MaxFacePicBytes = 204_800;

    public int Id { get; private set; }
    public string ExternalRef { get; private set; } = string.Empty;
    public string Name { get; private set; } = string.Empty;
    public AccessCode AccessCode { get; private set; } = null!;
    public byte[]? FacePic { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }

    private User() { } // for EF Core

    private User(string externalRef, string name, AccessCode accessCode, byte[]? facePic, DateTime now)
    {
        ExternalRef = externalRef;
        Name = name;
        AccessCode = accessCode;
        FacePic = facePic;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public static OneOf<User, ValidationError> Create(
        string? name,
        string? accessCode,
        byte[]? facePic,
        string? externalRef,
        DateTime now
    )
    {
        if (string.IsNullOrWhiteSpace(externalRef))
            return new ValidationError(Errors.ExternalRefField, Errors.ExternalRefRequired);
        if (externalRef.Length > 255)
            return new ValidationError(Errors.ExternalRefField, Errors.ExternalRefTooLong);

        if (string.IsNullOrWhiteSpace(name))
            return new ValidationError(Errors.NameField, Errors.NameRequired);
        if (name.Length > 100)
            return new ValidationError(Errors.NameField, Errors.NameTooLong);

        var codeResult = AccessCode.Create(accessCode);
        if (codeResult.TryPickT1(out var codeErr, out var code))
            return codeErr;

        if (facePic is not null && facePic.Length > MaxFacePicBytes)
            return new ValidationError(Errors.FacePicField, Errors.FacePicTooLarge);

        return new User(externalRef, name, code, facePic, now);
    }

    public OneOf<Success, ValidationError> Update(string? name, string? accessCode, byte[]? facePic)
    {
        if (name is not null)
        {
            if (name.Length == 0)
                return new ValidationError(Errors.NameField, Errors.NameEmpty);
            if (name.Length > 100)
                return new ValidationError(Errors.NameField, Errors.NameTooLong);
        }

        AccessCode? newCode = null;
        if (accessCode is not null)
        {
            var codeResult = AccessCode.Create(accessCode);
            if (codeResult.TryPickT1(out var codeErr, out var code))
                return codeErr;
            newCode = code;
        }

        if (facePic is not null && facePic.Length > MaxFacePicBytes)
            return new ValidationError(Errors.FacePicField, Errors.FacePicTooLarge);

        var changed = false;
        if (name is not null)
        {
            Name = name;
            changed = true;
        }
        if (newCode is not null)
        {
            AccessCode = newCode;
            changed = true;
        }
        if (facePic is not null)
        {
            FacePic = facePic;
            changed = true;
        }
        if (changed)
            UpdatedAt = DateTime.UtcNow;

        return new Success();
    }

    public static class Errors
    {
        public const string ExternalRefField = "externalRef";
        public const string ExternalRefRequired = "ExternalRef is required.";
        public const string ExternalRefTooLong = "ExternalRef must be 255 characters or fewer.";

        public const string NameField = "name";
        public const string NameRequired = "Name is required.";
        public const string NameEmpty = "Name cannot be empty.";
        public const string NameTooLong = "Name must be 100 characters or fewer.";

        public const string FacePicField = "facePic";
        public const string FacePicTooLarge = "Face picture must be at most 200 KB.";
    }
}
