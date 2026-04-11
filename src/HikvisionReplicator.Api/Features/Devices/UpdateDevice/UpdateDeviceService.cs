using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public class UpdateDeviceService(IRepository<Device> repo, IEncryptionService enc)
    : IUpdateDeviceService
{
    public async Task<
        OneOf<DeviceResponse, ValidationError, NotFoundError, ConflictError>
    > ExecuteAsync(int id, UpdateDeviceRequest req, CancellationToken cancellationToken)
    {
        var device = await repo.GetByIdAsync(id, cancellationToken);
        if (device is null)
            return new NotFoundError($"Device with id '{id}' was not found.");

        var originalIp = device.IpAddress.Value;
        var originalPort = device.HttpPort.Value;

        string? encryptedPassword = string.IsNullOrEmpty(req.Password)
            ? null
            : enc.Encrypt(req.Password);

        var updateResult = device.Update(
            req.Name,
            req.IpAddress,
            req.HttpPort,
            req.Username,
            encryptedPassword
        );
        if (updateResult.TryPickT1(out var validationError, out _))
            return validationError;

        if (device.IpAddress.Value != originalIp || device.HttpPort.Value != originalPort)
        {
            var conflict = await repo.AnyAsync(
                new DeviceByAddressSpec(device.IpAddress, device.HttpPort, id),
                cancellationToken
            );
            if (conflict)
                return new ConflictError(
                    $"A device with address {device.IpAddress.Value}:{device.HttpPort.Value} is already registered."
                );
        }

        await repo.UpdateAsync(device, cancellationToken);
        return DeviceResponse.FromEntity(device);
    }
}
