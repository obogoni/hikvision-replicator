using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

public class DeviceByAddressSpec : Specification<Device>
{
    public DeviceByAddressSpec(IpAddress ipAddress, Port httpPort, int? excludedId = null)
    {
        Query.Where(d => d.IpAddress == ipAddress && d.HttpPort == httpPort);
        if (excludedId.HasValue)
            Query.Where(d => d.Id != excludedId.Value);
    }
}
