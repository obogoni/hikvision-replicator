using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public record UpdateDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password);

public record DeviceResponse(
    int Id,
    string Name,
    string IpAddress,
    int HttpPort,
    string Username,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static DeviceResponse FromEntity(Device d) =>
        new(d.Id, d.Name, d.IpAddress.Value, d.HttpPort.Value, d.Username, d.CreatedAt, d.UpdatedAt);
}

public interface IUpdateDeviceService
{
    Task<OneOf<DeviceResponse, ValidationError, NotFoundError, ConflictError>> ExecuteAsync(int id, UpdateDeviceRequest request);
}
