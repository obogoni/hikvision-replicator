using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Api.Features.Devices.ListDevices;

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

/// <summary>
/// Listing cannot fail, so it returns the value directly rather than a
/// <c>OneOf</c> (AD-003). An empty catalogue is an empty list, never an error.
/// </summary>
public interface IListDevicesService
{
    Task<IReadOnlyList<DeviceResponse>> ExecuteAsync(CancellationToken cancellationToken);
}
