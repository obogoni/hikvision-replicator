using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Features.Devices.GetDevices;

public class GetDevicesService(IRepository<Device> repo) : IGetDevicesService
{
    public async Task<List<DeviceResponse>> ExecuteAsync()
    {
        var devices = await repo.ListAsync();
        return devices.Select(DeviceResponse.FromEntity).ToList();
    }
}
