using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Features.Devices.GetDevices;

public class GetDevicesService(IRepository<Device> repo) : IGetDevicesService
{
    public async Task<List<DeviceResponse>> ExecuteAsync(CancellationToken cancellationToken)
    {
        var devices = await repo.ListAsync(cancellationToken);
        return devices.Select(DeviceResponse.FromEntity).ToList();
    }
}
