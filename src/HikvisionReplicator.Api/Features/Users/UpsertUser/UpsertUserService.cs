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

        // The resurrection half arrives with T21. Until then a tombstoned reference cannot be
        // rewritten, and saying so is honest.
        if (existing.DeletedAt is not null)
            return new ConflictError(IUserRepository.ExternalRefAlreadyRegistered);

        return await UpdateAsync(existing, request, cancellationToken);
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
    /// Applies a corrected representation to a registered spectator.
    /// <para>
    /// <b>Every field of the representation is sent, not just the changed ones.</b> PUT is a
    /// full-representation upsert (A-2) and the face picture is its sole exception (A-4): omitting
    /// the picture keeps the stored image, omitting anything else is a rejection. This differs
    /// deliberately from the device slices, where a null means "leave unchanged" — devices are
    /// patched, spectators are replaced.
    /// </para>
    /// </summary>
    private async Task<
        OneOf<UserCreated, UserUpdated, ValidationError, ConflictError>
    > UpdateAsync(User user, UpsertUserRequest request, CancellationToken cancellationToken)
    {
        FaceFingerprint? fingerprint = null;
        byte[]? content = null;

        if (request.FacePicture is { Length: > 0 })
        {
            var faceResult = NormalizeFace(request.FacePicture);
            if (faceResult.TryPickT1(out var faceError, out var face))
                return faceError;

            (fingerprint, content) = face;

            // The stored picture is not loaded by any specification, so replacing it means asking
            // for it first: without the row in the graph the aggregate would build a second
            // picture for a user that already has one, and the write would fail on the 1:1 index
            // instead of overwriting the bytes (USR-25).
            await repository.LoadPictureAsync(user, cancellationToken);
        }

        // Update validates every field before assigning any of them, so a rejected correction
        // leaves the aggregate — and therefore the row and its image — untouched (USR-27). It
        // also advances UpdatedAt only when a value actually differs (USR-26).
        var updateResult = user.Update(
            request.Name,
            request.AccessCode,
            fingerprint,
            content,
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (updateResult.TryPickT1(out var validationError, out _))
            return validationError;

        // A spectator re-sending its own access code is never a conflict with itself (USR-28).
        if (
            await repository.AnyAsync(
                new ActiveUserByAccessCodeSpec(user.AccessCode, user.Id),
                cancellationToken
            )
        )
            return new ConflictError(IUserRepository.AccessCodeAlreadyInUse);

        var saveResult = await repository.SaveIfKeysFreeAsync(cancellationToken);
        if (saveResult.TryPickT1(out var conflictError, out _))
            return conflictError;

        return new UserUpdated(UserResponse.FromEntity(user));
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
