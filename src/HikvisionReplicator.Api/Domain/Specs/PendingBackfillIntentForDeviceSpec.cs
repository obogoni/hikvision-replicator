using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The debt a reader is still owed, if it is still owed one (REP-20).
/// <para>
/// <b>Pending only.</b> An expansion that found an already-<c>Expanded</c> intent and staged
/// the roster again would queue the fleet twice; finding nothing is what makes the second
/// invocation a no-op.
/// </para>
/// </summary>
public sealed class PendingBackfillIntentForDeviceSpec : Specification<BackfillIntent>
{
    public PendingBackfillIntentForDeviceSpec(int deviceId)
    {
        Query.Where(intent =>
            intent.DeviceId == deviceId && intent.Status == BackfillStatus.Pending
        );
    }
}
