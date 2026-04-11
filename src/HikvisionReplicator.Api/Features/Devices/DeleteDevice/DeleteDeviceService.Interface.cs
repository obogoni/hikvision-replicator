using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.DeleteDevice;

public interface IDeleteDeviceService
{
    Task<OneOf<Success, NotFoundError>> ExecuteAsync(int id);
}
