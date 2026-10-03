using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public class UpdateDeviceService(
    IDeviceRepository repository,
    IUserRepository users,
    IEncryptionService encryptionService,
    TimeProvider timeProvider
) : IUpdateDeviceService
{
    public const string DeviceNotFound = "Device not found.";
    public const string PasswordField = "password";
    public const string PasswordEmpty = "Password cannot be empty.";

    public async Task<
        OneOf<DeviceResponse, ValidationError, NotFoundError, ConflictError>
    > ExecuteAsync(int id, UpdateDeviceRequest request, CancellationToken cancellationToken)
    {
        var device = await repository.GetByIdAsync(id, cancellationToken);
        if (device is null)
            return new NotFoundError(DeviceNotFound);

        // Omitting the password leaves the stored ciphertext alone; supplying one
        // replaces it. There is no way to clear it, since it is mandatory (A-7).
        string? encryptedPassword = null;
        if (request.Password is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Password))
                return new ValidationError(PasswordField, PasswordEmpty);

            encryptedPassword = encryptionService.Encrypt(request.Password);
        }

        var capacityBefore = device.FaceCapacity.Value;

        // Update validates every field before assigning any of them, so a rejected
        // update leaves the aggregate — and therefore the row — untouched (DEV-19).
        var updateResult = device.Update(
            request.Name,
            request.IpAddress,
            request.HttpPort,
            request.Username,
            encryptedPassword,
            request.FaceCapacity,
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (updateResult.TryPickT1(out var validationError, out _))
            return validationError;

        // Only a *change* to the ceiling is admission-checked (REP-23). A reader the roster
        // has outgrown since it was registered keeps its place — that is REP-24's signal, not
        // a refusal — so renaming it must not fail for a capacity nobody touched.
        if (device.FaceCapacity.Value != capacityBefore)
        {
            var tooSmall = await FleetAdmission.RefuseIfTooSmallAsync(
                users,
                device.FaceCapacity.Value,
                cancellationToken
            );
            if (tooSmall is not null)
                return tooSmall;
        }

        // The device's own address is never a conflict with itself (DEV-20).
        var addressTaken = await repository.AnyAsync(
            new DeviceByAddressExcludingSpec(device.IpAddress, device.HttpPort, device.Id),
            cancellationToken
        );
        if (addressTaken)
            return new ConflictError(IDeviceRepository.AddressAlreadyRegistered);

        var saveResult = await repository.SaveIfAddressFreeAsync(cancellationToken);
        if (saveResult.TryPickT1(out var conflictError, out _))
            return conflictError;

        return DeviceResponse.FromEntity(device);
    }
}
