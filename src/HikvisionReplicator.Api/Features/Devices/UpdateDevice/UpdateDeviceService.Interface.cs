using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

/// <summary>A null field means "leave unchanged" (DEV-18, A-7).</summary>
public record UpdateDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password,
    int? FaceCapacity
);

/// <summary>Carries no password field of any kind (DEV-07).</summary>
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

public interface IUpdateDeviceService
{
    Task<OneOf<DeviceResponse, ValidationError, NotFoundError, ConflictError>> ExecuteAsync(
        int id,
        UpdateDeviceRequest request,
        CancellationToken cancellationToken
    );
}
