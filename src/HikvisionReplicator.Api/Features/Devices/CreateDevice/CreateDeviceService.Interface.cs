namespace HikvisionReplicator.Api.Features.Devices.CreateDevice;

public record CreateDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password);

public interface ICreateDeviceService
{
    Task<IResult> ExecuteAsync(CreateDeviceRequest request);
}
