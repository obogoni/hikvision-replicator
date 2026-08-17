using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.RemoveDevice;

public interface IRemoveDeviceService
{
    Task<OneOf<Success, NotFoundError>> ExecuteAsync(int id, CancellationToken cancellationToken);
}
