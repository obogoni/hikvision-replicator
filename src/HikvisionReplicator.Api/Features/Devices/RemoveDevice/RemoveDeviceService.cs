using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.RemoveDevice;

public class RemoveDeviceService(IDeviceRepository repository) : IRemoveDeviceService
{
    public const string DeviceNotFound = "Device not found.";

    /// <summary>
    /// A hard delete, so the row — and with it the unique-index entry holding the
    /// address — is gone and the address becomes available again (DEV-25).
    /// </summary>
    public async Task<OneOf<Success, NotFoundError>> ExecuteAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var device = await repository.GetByIdAsync(id, cancellationToken);
        if (device is null)
            return new NotFoundError(DeviceNotFound);

        await repository.DeleteAsync(device, cancellationToken);

        return new Success();
    }
}
