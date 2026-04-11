using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.GetDevice;

public class GetDeviceService(IRepository<Device> repo) : IGetDeviceService
{
    public async Task<OneOf<DeviceResponse, NotFoundError>> ExecuteAsync(
        int id,
        CancellationToken cancellationToken
    )
    {
        var device = await repo.GetByIdAsync(id, cancellationToken);
        if (device is null)
            return new NotFoundError($"Device with id '{id}' was not found.");

        return DeviceResponse.FromEntity(device);
    }
}
