using System.Text.RegularExpressions;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;
using HikvisionReplicator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Features.Devices.CreateDevice;

public class CreateDeviceService(AppDbContext db, IEncryptionService enc) : ICreateDeviceService
{
    private static readonly Regex Ipv4Regex =
        new(@"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)$",
            RegexOptions.Compiled);

    public async Task<IResult> ExecuteAsync(CreateDeviceRequest req)
    {
        var errors = Validate(req);
        if (errors.Count > 0)
            return Results.BadRequest(ErrorResponse.Validation(errors));

        var conflict = await db.Devices.AnyAsync(d =>
            d.IpAddress == req.IpAddress && d.HttpPort == req.HttpPort!.Value);
        if (conflict)
            return Results.Conflict(ErrorResponse.Conflict(
                $"A device with address {req.IpAddress}:{req.HttpPort} is already registered."));

        var now = DateTime.UtcNow;
        var device = new Device
        {
            Name = req.Name!,
            IpAddress = req.IpAddress!,
            HttpPort = req.HttpPort!.Value,
            Username = req.Username!,
            EncryptedPassword = enc.Encrypt(req.Password!),
            CreatedAt = now,
            UpdatedAt = now
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync();

        return Results.Created($"/api/devices/{device.Id}", DeviceResponse.FromEntity(device));
    }

    private static Dictionary<string, string[]> Validate(CreateDeviceRequest req)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(req.Name))
            errors["name"] = ["Name is required."];
        else if (req.Name.Length > 100)
            errors["name"] = ["Name must be 100 characters or fewer."];

        if (string.IsNullOrWhiteSpace(req.IpAddress))
            errors["ipAddress"] = ["IpAddress is required."];
        else if (!Ipv4Regex.IsMatch(req.IpAddress))
            errors["ipAddress"] = ["Must be a valid IPv4 address."];

        if (req.HttpPort is null)
            errors["httpPort"] = ["HttpPort is required."];
        else if (req.HttpPort < 1 || req.HttpPort > 65535)
            errors["httpPort"] = ["Must be between 1 and 65535."];

        if (string.IsNullOrWhiteSpace(req.Username))
            errors["username"] = ["Username is required."];
        else if (req.Username.Length > 100)
            errors["username"] = ["Username must be 100 characters or fewer."];

        if (string.IsNullOrWhiteSpace(req.Password))
            errors["password"] = ["Password is required."];

        return errors;
    }
}
