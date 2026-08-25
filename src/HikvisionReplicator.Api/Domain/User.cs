using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Domain;

/// <summary>
/// A spectator. Owns every identity invariant and the tombstone transition. The clock is
/// always passed in, never read (AD-023), and the face picture arrives already normalized.
/// </summary>
public class User : IAggregateRoot
{
    public const int MaxNameLength = 100;

    public int Id { get; private set; }
    public ExternalRef ExternalRef { get; private set; } = null!;
    public string Name { get; private set; } = string.Empty;
    public AccessCode AccessCode { get; private set; } = null!;
    public FaceFingerprint Face { get; private set; } = null!;
    public DateTime? DeletedAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime UpdatedAt { get; private set; }
    public FacePicture? Picture { get; private set; }

    private User() { } // for EF Core

    private User(
        ExternalRef externalRef,
        string name,
        AccessCode accessCode,
        FaceFingerprint face,
        byte[] pictureContent,
        DateTime now
    )
    {
        ExternalRef = externalRef;
        Name = name;
        AccessCode = accessCode;
        Face = face;
        Picture = FacePicture.ForUser(pictureContent);
        CreatedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Registers a spectator from an already-normalized face picture. A user cannot exist
    /// without one (A-3), so the fingerprint and its bytes are required arguments.
    /// </summary>
    public static OneOf<User, ValidationError> Create(
        string? externalRef,
        string? name,
        string? accessCode,
        FaceFingerprint fingerprint,
        byte[] pictureContent,
        DateTime now
    )
    {
        var refResult = ExternalRef.Create(externalRef);
        if (refResult.TryPickT1(out var refError, out var reference))
            return refError;

        var nameResult = ValidateName(name);
        if (nameResult.TryPickT1(out var nameError, out var trimmedName))
            return nameError;

        var codeResult = AccessCode.Create(accessCode);
        if (codeResult.TryPickT1(out var codeError, out var code))
            return codeError;

        return new User(reference, trimmedName, code, fingerprint, pictureContent, now);
    }

    /// <summary>
    /// Applies a corrected representation. A null fingerprint/content pair means "keep the
    /// stored image" (USR-24). Every field is validated before any is assigned, so a rejected
    /// update leaves the aggregate exactly as it was (USR-27). <see cref="UpdatedAt"/> advances
    /// only when a value actually differs from the current one (USR-26).
    /// </summary>
    public OneOf<Success, ValidationError> Update(
        string? name,
        string? accessCode,
        FaceFingerprint? fingerprint,
        byte[]? pictureContent,
        DateTime now
    )
    {
        // ── Validate everything first — no assignment happens above this line ──
        var nameResult = ValidateName(name);
        if (nameResult.TryPickT1(out var nameError, out var trimmedName))
            return nameError;

        var codeResult = AccessCode.Create(accessCode);
        if (codeResult.TryPickT1(out var codeError, out var code))
            return codeError;

        // ── Everything is valid: apply only what actually differs ──
        var changed = false;

        if (trimmedName != Name)
        {
            Name = trimmedName;
            changed = true;
        }

        if (code != AccessCode)
        {
            AccessCode = code;
            changed = true;
        }

        if (fingerprint is not null && fingerprint != Face)
        {
            Face = fingerprint;
            SetPicture(pictureContent!);
            changed = true;
        }

        if (changed)
            UpdatedAt = now;

        return new Success();
    }

    private void SetPicture(byte[] content)
    {
        if (Picture is null)
            Picture = FacePicture.ForUser(content);
        else
            Picture.Replace(content);
    }

    private static OneOf<string, ValidationError> ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return new ValidationError(Errors.NameField, Errors.NameRequired);

        // Trimmed before the length check, so trailing whitespace cannot push an
        // otherwise-acceptable name over the limit (spec Edge Cases).
        var trimmed = name.Trim();
        if (trimmed.Length > MaxNameLength)
            return new ValidationError(Errors.NameField, Errors.NameTooLong);

        return trimmed;
    }

    public static class Errors
    {
        public const string NameField = "name";
        public const string NameRequired = "Name is required.";
        public const string NameTooLong = "Name must be 100 characters or fewer.";
    }
}
