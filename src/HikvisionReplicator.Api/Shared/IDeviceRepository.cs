using HikvisionReplicator.Api.Domain;
using OneOf;

namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Persists devices with the address-uniqueness invariant enforced by the database.
/// The constraint violation is translated into a <see cref="ConflictError"/> here, so no
/// slice ever sees a provider exception (AD-022).
/// </summary>
public interface IDeviceRepository : IRepository<Device>
{
    /// <summary>
    /// The single message for a taken address, shared by the pre-check and the
    /// constraint translation so both paths are indistinguishable to the caller.
    /// </summary>
    const string AddressAlreadyRegistered = "A device is already registered at this address.";

    /// <summary>Inserts the device, or reports the address as taken (DEV-05, DEV-06).</summary>
    Task<OneOf<Success, ConflictError>> AddIfAddressFreeAsync(
        Device device,
        CancellationToken cancellationToken
    );

    /// <summary>Saves pending changes, or reports the address as taken (DEV-20).</summary>
    Task<OneOf<Success, ConflictError>> SaveIfAddressFreeAsync(CancellationToken cancellationToken);
}
