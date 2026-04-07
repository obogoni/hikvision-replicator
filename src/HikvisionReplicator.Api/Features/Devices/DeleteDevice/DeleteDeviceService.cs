using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;

namespace HikvisionReplicator.Api.Features.Devices.DeleteDevice;

public class DeleteDeviceService(AppDbContext db) : IDeleteDeviceService
{
    public async Task<IResult> ExecuteAsync(int id)
    {
        var device = await db.Devices.FindAsync(id);
        if (device is null)
            return Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."));

        db.Devices.Remove(device);
        await db.SaveChangesAsync();
        return Results.NoContent();
    }
}
