using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Features.Devices.GetDevices;

public class GetDevicesService(AppDbContext db) : IGetDevicesService
{
    public async Task<IResult> ExecuteAsync()
    {
        var devices = await db.Devices
            .Select(d => DeviceResponse.FromEntity(d))
            .ToListAsync();
        return Results.Ok(devices);
    }
}
