using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Features.Devices.GetDevice;

public record DeviceResponse(
    int Id,
    string Name,
    string IpAddress,
    int HttpPort,
    string Username,
    DateTime CreatedAt,
    DateTime UpdatedAt
)
{
    public static DeviceResponse FromEntity(Device d) =>
        new(
            d.Id,
            d.Name,
            d.IpAddress.Value,
            d.HttpPort.Value,
            d.Username,
            d.CreatedAt,
            d.UpdatedAt
        );
}

public interface IGetDeviceService
{
    Task<OneOf<DeviceResponse, NotFoundError>> ExecuteAsync(
        int id,
        CancellationToken cancellationToken
    );
}
