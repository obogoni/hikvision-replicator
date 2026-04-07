using System.Text.RegularExpressions;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public class UpdateDeviceService(AppDbContext db, IEncryptionService enc) : IUpdateDeviceService
{
    private static readonly Regex Ipv4Regex =
        new(@"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)$",
            RegexOptions.Compiled);

    public async Task<IResult> ExecuteAsync(int id, UpdateDeviceRequest req)
    {
        var device = await db.Devices.FindAsync(id);
        if (device is null)
            return Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."));

        var errors = Validate(req);
        if (errors.Count > 0)
            return Results.BadRequest(ErrorResponse.Validation(errors));

        var newIp = req.IpAddress ?? device.IpAddress;
        var newPort = req.HttpPort ?? device.HttpPort;

        if ((req.IpAddress != null || req.HttpPort != null) &&
            (newIp != device.IpAddress || newPort != device.HttpPort))
        {
            var conflict = await db.Devices.AnyAsync(d =>
                d.Id != id && d.IpAddress == newIp && d.HttpPort == newPort);
            if (conflict)
                return Results.Conflict(ErrorResponse.Conflict(
                    $"A device with address {newIp}:{newPort} is already registered."));
        }

        if (req.Name != null) device.Name = req.Name;
        if (req.IpAddress != null) device.IpAddress = req.IpAddress;
        if (req.HttpPort != null) device.HttpPort = req.HttpPort.Value;
        if (req.Username != null) device.Username = req.Username;
        if (!string.IsNullOrEmpty(req.Password)) device.EncryptedPassword = enc.Encrypt(req.Password);
        device.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return Results.Ok(DeviceResponse.FromEntity(device));
    }

    private static Dictionary<string, string[]> Validate(UpdateDeviceRequest req)
    {
        var errors = new Dictionary<string, string[]>();

        if (req.Name != null)
        {
            if (req.Name.Length == 0)
                errors["name"] = ["Name cannot be empty."];
            else if (req.Name.Length > 100)
                errors["name"] = ["Name must be 100 characters or fewer."];
        }

        if (req.IpAddress != null && !Ipv4Regex.IsMatch(req.IpAddress))
            errors["ipAddress"] = ["Must be a valid IPv4 address."];

        if (req.HttpPort != null && (req.HttpPort < 1 || req.HttpPort > 65535))
            errors["httpPort"] = ["Must be between 1 and 65535."];

        if (req.Username != null)
        {
            if (req.Username.Length == 0)
                errors["username"] = ["Username cannot be empty."];
            else if (req.Username.Length > 100)
                errors["username"] = ["Username must be 100 characters or fewer."];
        }

        if (req.Password != null && req.Password.Length == 0)
            errors["password"] = ["Password cannot be empty if provided."];

        return errors;
    }
}
