using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public class UpdateDeviceService(AppDbContext db, IEncryptionService enc) : IUpdateDeviceService
{
    public async Task<IResult> ExecuteAsync(int id, UpdateDeviceRequest req)
    {
        var device = await db.Devices.FindAsync(id);
        if (device is null)
            return Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."));

        var originalIp = device.IpAddress.Value;
        var originalPort = device.HttpPort.Value;

        string? encryptedPassword = string.IsNullOrEmpty(req.Password)
            ? null
            : enc.Encrypt(req.Password);

        var updateResult = device.Update(req.Name, req.IpAddress, req.HttpPort, req.Username, encryptedPassword);
        if (updateResult.IsFailure)
            return Results.BadRequest(ErrorResponse.Validation(
                new Dictionary<string, string[]> { [updateResult.Error.Field] = [updateResult.Error.Message] }));

        if (device.IpAddress.Value != originalIp || device.HttpPort.Value != originalPort)
        {
            var conflict = await db.Devices.AnyAsync(d =>
                d.Id != id && d.IpAddress == device.IpAddress && d.HttpPort == device.HttpPort);
            if (conflict)
                return Results.Conflict(ErrorResponse.Conflict(
                    $"A device with address {device.IpAddress.Value}:{device.HttpPort.Value} is already registered."));
        }

        await db.SaveChangesAsync();
        return Results.Ok(DeviceResponse.FromEntity(device));
    }
}
