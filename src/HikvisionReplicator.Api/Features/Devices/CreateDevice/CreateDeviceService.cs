using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.CreateDevice;

public class CreateDeviceService(IRepository<Device> repo, IEncryptionService enc)
    : ICreateDeviceService
{
    public async Task<OneOf<DeviceResponse, ValidationError, ConflictError>> ExecuteAsync(
        CreateDeviceRequest req,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(req.Password))
            return new ValidationError("password", "Password is required.");

        var encryptedPassword = enc.Encrypt(req.Password!);
        var now = DateTime.UtcNow;

        var deviceResult = Device.Create(
            req.Name,
            req.IpAddress,
            req.HttpPort,
            req.Username,
            encryptedPassword,
            now
        );
        if (deviceResult.TryPickT1(out var validationError, out var device))
            return validationError;

        var conflict = await repo.AnyAsync(
            new DeviceByAddressSpec(device.IpAddress, device.HttpPort),
            cancellationToken
        );
        if (conflict)
            return new ConflictError(
                $"A device with address {req.IpAddress}:{req.HttpPort} is already registered."
            );

        await repo.AddAsync(device, cancellationToken);

        return DeviceResponse.FromEntity(device);
    }
}
