using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.RegisterDevice;

public record RegisterDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password,
    int? FaceCapacity
);

/// <summary>
/// The registration view of a device. It carries no password field of any kind — neither
/// the plaintext nor its ciphertext ever leaves the process (DEV-07).
/// </summary>
public record DeviceResponse(
    int Id,
    string Name,
    string IpAddress,
    int HttpPort,
    string Username,
    int FaceCapacity,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static DeviceResponse FromEntity(Device device) =>
        new(
            device.Id,
            device.Name,
            device.IpAddress.Value,
            device.HttpPort.Value,
            device.Username,
            device.FaceCapacity.Value,
            device.CreatedAt,
            device.UpdatedAt
        );
}

public interface IRegisterDeviceService
{
    Task<OneOf<DeviceResponse, ValidationError, ConflictError>> ExecuteAsync(
        RegisterDeviceRequest request,
        CancellationToken cancellationToken
    );
}
