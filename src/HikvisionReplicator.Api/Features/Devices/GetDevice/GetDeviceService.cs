using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;

namespace HikvisionReplicator.Api.Features.Devices.GetDevice;

public class GetDeviceService(AppDbContext db) : IGetDeviceService
{
    public async Task<IResult> ExecuteAsync(int id)
    {
        var device = await db.Devices.FindAsync(id);
        return device is null
            ? Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."))
            : Results.Ok(DeviceResponse.FromEntity(device));
    }
}
