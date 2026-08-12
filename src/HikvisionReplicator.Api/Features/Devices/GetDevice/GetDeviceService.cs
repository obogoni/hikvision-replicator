using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.GetDevice;

public class GetDeviceService(IDeviceRepository repository) : IGetDeviceService
{
    /// <summary>
    /// Deliberately mentions no id, so an id that never existed and one that was
    /// removed are indistinguishable to the caller (DEV-10).
    /// </summary>
    public const string DeviceNotFound = "Device not found.";

    public async Task<OneOf<DeviceResponse, NotFoundError>> ExecuteAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var device = await repository.GetByIdAsync(id, cancellationToken);

        return device is null
            ? new NotFoundError(DeviceNotFound)
            : DeviceResponse.FromEntity(device);
    }
}
