using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.RegisterDevice;

public class RegisterDeviceService(
    IDeviceRepository repository,
    IEncryptionService encryptionService,
    TimeProvider timeProvider
) : IRegisterDeviceService
{
    public const string PasswordField = "password";
    public const string PasswordRequired = "Password is required.";

    public async Task<OneOf<DeviceResponse, ValidationError, ConflictError>> ExecuteAsync(
        RegisterDeviceRequest request,
        CancellationToken cancellationToken
    )
    {
        // The aggregate only ever sees ciphertext, so the plaintext rule lives here.
        if (string.IsNullOrWhiteSpace(request.Password))
            return new ValidationError(PasswordField, PasswordRequired);

        var deviceResult = Device.Create(
            request.Name,
            request.IpAddress,
            request.HttpPort,
            request.Username,
            encryptionService.Encrypt(request.Password),
            request.FaceCapacity,
            timeProvider.GetUtcNow().UtcDateTime
        );
        if (deviceResult.TryPickT1(out var validationError, out var device))
            return validationError;

        // A friendly answer on the common path. The unique index remains the authority,
        // so a registration that slips past this check still comes back as a conflict.
        var addressTaken = await repository.AnyAsync(
            new DeviceByAddressSpec(device.IpAddress, device.HttpPort),
            cancellationToken
        );
        if (addressTaken)
            return new ConflictError(IDeviceRepository.AddressAlreadyRegistered);

        var saveResult = await repository.AddIfAddressFreeAsync(device, cancellationToken);
        if (saveResult.TryPickT1(out var conflictError, out _))
            return conflictError;

        return DeviceResponse.FromEntity(device);
    }
}
