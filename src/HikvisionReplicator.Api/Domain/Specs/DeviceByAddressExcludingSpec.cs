using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// Another device holding a network address, ignoring the device being updated — so
/// re-submitting a device's own address is not a conflict with itself (DEV-20).
/// </summary>
public sealed class DeviceByAddressExcludingSpec : Specification<Device>
{
    public DeviceByAddressExcludingSpec(IpAddress ipAddress, Port httpPort, int excludedDeviceId)
    {
        Query.Where(device =>
            device.IpAddress == ipAddress
            && device.HttpPort == httpPort
            && device.Id != excludedDeviceId
        );
    }
}
