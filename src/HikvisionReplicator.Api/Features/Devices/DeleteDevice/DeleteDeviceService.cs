using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.DeleteDevice;

public class DeleteDeviceService(IRepository<Device> repo) : IDeleteDeviceService
{
    public async Task<OneOf<Success, NotFoundError>> ExecuteAsync(int id)
    {
        var device = await repo.GetByIdAsync(id);
        if (device is null)
            return new NotFoundError($"Device with id '{id}' was not found.");

        await repo.DeleteAsync(device);
        return new Success();
    }
}
