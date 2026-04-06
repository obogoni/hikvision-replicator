namespace HikvisionReplicator.Api.Features.Devices;

public record CreateDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password);

public record UpdateDeviceRequest(
    string? Name,
    string? IpAddress,
    int? HttpPort,
    string? Username,
    string? Password);
