using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Features.Devices.ListDevices;

public class ListDevicesService(IDeviceRepository repository) : IListDevicesService
{
    public async Task<IReadOnlyList<DeviceResponse>> ExecuteAsync(
        CancellationToken cancellationToken
    )
    {
        var devices = await repository.ListAsync(cancellationToken);

        return devices.Select(DeviceResponse.FromEntity).ToList();
    }
}
