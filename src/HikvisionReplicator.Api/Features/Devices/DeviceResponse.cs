using HikvisionReplicator.Data.Entities;

namespace HikvisionReplicator.Api.Features.Devices;

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
        new(d.Id, d.Name, d.IpAddress, d.HttpPort, d.Username, d.CreatedAt, d.UpdatedAt);
}
