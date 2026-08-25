using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

public class UpsertUserService(
    IUserRepository repository,
    IFaceImageNormalizer normalizer,
    TimeProvider timeProvider
) : IUpsertUserService
{
    public async Task<
        OneOf<UserCreated, UserUpdated, ValidationError, ConflictError>
    > ExecuteAsync(
        string? externalRef,
        UpsertUserRequest request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);

        var refResult = ExternalRef.Create(externalRef);
        if (refResult.TryPickT1(out var refError, out var reference))
            return refError;

        // Tombstones included, deliberately: the external reference stays reserved after
        // deletion, so a PUT naming a deleted spectator is a resurrection rather than a create
        // (A-7). The active-only specification would report it as unregistered and then collide
        // with the index.
        var existing = await repository.FirstOrDefaultAsync(
            new UserByExternalRefIncludingDeletedSpec(reference),
            cancellationToken
        );

        if (existing is null)
            return await CreateAsync(externalRef, request, cancellationToken);

        // The update and resurrection halves arrive with T18 and T21. Until then an existing
        // reference cannot be rewritten, and saying so is honest.
        return new ConflictError(IUserRepository.ExternalRefAlreadyRegistered);
    }

    private async Task<
        OneOf<UserCreated, UserUpdated, ValidationError, ConflictError>
    > CreateAsync(
        string? externalRef,
        UpsertUserRequest request,
        CancellationToken cancellationToken
    )
    {
        // A-3: a spectator cannot exist without a face, so this is checked before anything is
        // spent on the rest of the representation.
        if (request.FacePicture is null || request.FacePicture.Length == 0)
            return new ValidationError(FaceFingerprint.Errors.Field, User.Errors.PictureRequired);

        var faceResult = NormalizeFace(request.FacePicture);
        if (faceResult.TryPickT1(out var faceError, out var face))
            return faceError;

        var userResult = User.Create(
            externalRef,
            request.Name,
            request.AccessCode,
            face.Fingerprint,
            face.Content,
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (userResult.TryPickT1(out var validationError, out var user))
            return validationError;

        // A friendly answer on the common path. The partial unique index remains the authority,
        // so a registration that slips past this check still comes back as a conflict (AD-022).
        if (
            await repository.AnyAsync(
                new ActiveUserByAccessCodeSpec(user.AccessCode),
                cancellationToken
            )
        )
            return new ConflictError(IUserRepository.AccessCodeAlreadyInUse);

        // One SaveChanges, so the user row and its face picture are written in a single
        // transaction: a failed picture write leaves no user behind (USR-10).
        var saveResult = await repository.AddIfKeysFreeAsync(user, cancellationToken);
        if (saveResult.TryPickT1(out var conflictError, out _))
            return conflictError;

        return new UserCreated(UserResponse.FromEntity(user));
    }

    /// <summary>
    /// Normalizes the upload and turns it into the pair the aggregate accepts. The aggregate only
    /// ever sees an already-canonical image, exactly as it only ever sees an encrypted password
    /// on the device side (AD-008).
    /// </summary>
    private OneOf<(FaceFingerprint Fingerprint, byte[] Content), ValidationError> NormalizeFace(
        byte[] upload
    )
    {
        var normalized = normalizer.Normalize(upload);
        if (normalized.TryPickT1(out var imageError, out var image))
            return imageError;

        var fingerprintResult = FaceFingerprint.Create(
            image.ContentHash,
            image.Content.Length,
            image.Width,
            image.Height
        );

        return fingerprintResult.TryPickT1(out var fingerprintError, out var fingerprint)
            ? fingerprintError
            : (fingerprint, image.Content);
    }
}
