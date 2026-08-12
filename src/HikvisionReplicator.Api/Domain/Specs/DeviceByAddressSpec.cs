using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The device holding a network address. Used as the registration pre-check, which
/// exists only to produce a friendly conflict message — the unique index is the
/// authority (AD-022).
/// </summary>
public sealed class DeviceByAddressSpec : Specification<Device>
{
    public DeviceByAddressSpec(IpAddress ipAddress, Port httpPort)
    {
        Query.Where(device => device.IpAddress == ipAddress && device.HttpPort == httpPort);
    }
}
