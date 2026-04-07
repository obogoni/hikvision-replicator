namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public record UpdateDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password);

public interface IUpdateDeviceService
{
    Task<IResult> ExecuteAsync(int id, UpdateDeviceRequest request);
}
