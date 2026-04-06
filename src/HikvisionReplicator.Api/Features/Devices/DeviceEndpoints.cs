using System.Text.RegularExpressions;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Data;
using HikvisionReplicator.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Features.Devices;

public static class DeviceEndpoints
{
    private static readonly Regex Ipv4Regex =
        new(@"^((25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d{2}|[1-9]?\d)$",
            RegexOptions.Compiled);

    public static void MapDevicesEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/devices");

        // POST /api/devices — US1: Register a device
        group.MapPost("/", async (CreateDeviceRequest req, AppDbContext db, IEncryptionService enc) =>
        {
            var errors = ValidateCreate(req);
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

            var response = DeviceResponse.FromEntity(device);
            return Results.Created($"/api/devices/{device.Id}", response);
        });

        // GET /api/devices — US2: List all devices
        group.MapGet("/", async (AppDbContext db) =>
        {
            var devices = await db.Devices
                .Select(d => DeviceResponse.FromEntity(d))
                .ToListAsync();
            return Results.Ok(devices);
        });

        // GET /api/devices/{id} — US2: Get a device by ID
        group.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var device = await db.Devices.FindAsync(id);
            return device is null
                ? Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."))
                : Results.Ok(DeviceResponse.FromEntity(device));
        });

        // PUT /api/devices/{id} — US3: Update a device
        group.MapPut("/{id:int}", async (int id, UpdateDeviceRequest req, AppDbContext db, IEncryptionService enc) =>
        {
            var device = await db.Devices.FindAsync(id);
            if (device is null)
                return Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."));

            var errors = ValidateUpdate(req);
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
        });

        // DELETE /api/devices/{id} — US4: Delete a device
        group.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
        {
            var device = await db.Devices.FindAsync(id);
            if (device is null)
                return Results.NotFound(ErrorResponse.NotFound($"Device with id '{id}' was not found."));

            db.Devices.Remove(device);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });
    }

    private static Dictionary<string, string[]> ValidateCreate(CreateDeviceRequest req)
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

    private static Dictionary<string, string[]> ValidateUpdate(UpdateDeviceRequest req)
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
