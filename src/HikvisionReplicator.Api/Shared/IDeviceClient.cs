using OneOf;

namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Where and how to reach one reader, as the catalogue held it right before the call (WRK-33).
/// The password is plaintext, decrypted for this call only (AD-008), so this record never
/// leaves the worker and is never logged.
/// </summary>
public sealed record DeviceEndpoint(
    int DeviceId,
    string IpAddress,
    int Port,
    string Username,
    string Password
);

/// <summary>
/// What a reader needs to recognise a spectator at the turnstile (WRK-02): the key the
/// integrator knows them by, the PIN, and the normalized face.
/// </summary>
public sealed record DeviceEnrolment(
    string ExternalRef,
    string AccessCode,
    ReadOnlyMemory<byte> FacePicture
);

/// <summary>
/// The one boundary to a reader (AD-017). Every call ends in exactly one of three answers and
/// the worker's whole failure model hangs on which: <see cref="Delivered"/>,
/// <see cref="Rejected"/> (the reader is up and refused the record) or
/// <see cref="Unreachable"/> (it never judged it). A call that outlives the worker's timeout is
/// cancelled through the token and counts as unreachable (WRK-11).
/// </summary>
public interface IDeviceClient
{
    Task<OneOf<Delivered, Rejected, Unreachable>> AddAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    );

    Task<OneOf<Delivered, Rejected, Unreachable>> UpdateAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    );

    /// <summary>A removal carries the external reference only — the face is already gone (WRK-02).</summary>
    Task<OneOf<Delivered, Rejected, Unreachable>> RemoveAsync(
        DeviceEndpoint device,
        string externalRef,
        CancellationToken cancellationToken
    );
}
