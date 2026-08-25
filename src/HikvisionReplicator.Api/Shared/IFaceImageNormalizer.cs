using OneOf;

namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Turns any reasonable upload into an image the device will enrol, or explains why it cannot.
/// </summary>
public interface IFaceImageNormalizer
{
    /// <summary>
    /// Normalizes <paramref name="upload"/> into the device's accepted envelope, or returns the
    /// reason it cannot be.
    /// </summary>
    /// <remarks>
    /// <b>Synchronous and cancellation-free by design — this is not an oversight.</b> AD-007 puts a
    /// <c>CancellationToken</c> on every asynchronous boundary, but normalization is pure CPU work
    /// with no I/O: there is no await point for a token to trip, so one here would be decoration
    /// that a caller could reasonably mistake for a guarantee. If normalization ever grows an I/O
    /// step, add the token then — do not add it now to satisfy the shape of a rule whose purpose
    /// this signature does not engage.
    /// </remarks>
    OneOf<NormalizedFaceImage, ValidationError> Normalize(byte[] upload);
}

/// <summary>
/// A canonical JPEG derivative inside the device's envelope, with the fingerprint the catalogue
/// needs to detect a changed face without reading the bytes back (USR-22).
/// </summary>
/// <param name="Content">The canonical JPEG bytes. Never the original upload.</param>
/// <param name="ContentHash">SHA-256 over <paramref name="Content"/>.</param>
/// <param name="Width">Width of the derivative in pixels, after orientation and any downscale.</param>
/// <param name="Height">Height of the derivative in pixels, after orientation and any downscale.</param>
public sealed record NormalizedFaceImage(
    byte[] Content,
    string ContentHash,
    int Width,
    int Height
);
