using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>How many faces one reader in the catalogue can hold, and which reader it is.</summary>
public sealed record ReaderCeiling(int DeviceId, FaceCapacity FaceCapacity);

/// <summary>
/// Every reader's ceiling — what REP-24 compares the roster against.
/// <para>
/// A projection rather than the aggregates, for the same reason
/// <see cref="RegisteredDeviceIdsSpec"/> is one: this read sits on the spectator write path
/// AD-014 makes the primary quality attribute, and the ceilings are two integers each.
/// </para>
/// <para>
/// It does not filter. <c>FaceCapacity</c> is persisted through a value converter, so a
/// <c>&lt;</c> against it is not a comparison the provider can translate — and at AD-038's
/// envelope of twenty readers the comparison costs nothing to make in memory.
/// </para>
/// </summary>
public sealed class ReaderCeilingsSpec : Specification<Device, ReaderCeiling>
{
    public ReaderCeilingsSpec()
    {
        Query.Select(device => new ReaderCeiling(device.Id, device.FaceCapacity));
    }
}
