using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Features.Devices.CreateDevice;

public class CreateDeviceService(AppDbContext db, IEncryptionService enc) : ICreateDeviceService
{
    public async Task<IResult> ExecuteAsync(CreateDeviceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Password))
            return Results.BadRequest(ErrorResponse.Validation(
                new Dictionary<string, string[]> { ["password"] = ["Password is required."] }));

        var encryptedPassword = enc.Encrypt(req.Password!);
        var now = DateTime.UtcNow;

        var deviceResult = Device.Create(req.Name, req.IpAddress, req.HttpPort, req.Username, encryptedPassword, now);
        if (deviceResult.IsFailure)
            return Results.BadRequest(ErrorResponse.Validation(
                new Dictionary<string, string[]> { [deviceResult.Error.Field] = [deviceResult.Error.Message] }));

        var device = deviceResult.Value;

        var conflict = await db.Devices.AnyAsync(d =>
            d.IpAddress == device.IpAddress && d.HttpPort == device.HttpPort);
        if (conflict)
            return Results.Conflict(ErrorResponse.Conflict(
                $"A device with address {req.IpAddress}:{req.HttpPort} is already registered."));

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        return Results.Created($"/api/devices/{device.Id}", DeviceResponse.FromEntity(device));
    }
}
