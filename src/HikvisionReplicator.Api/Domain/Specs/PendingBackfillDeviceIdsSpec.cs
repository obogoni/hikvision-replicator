using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// Every reader still owed its backfill — the debts the worker expands on its next tick
/// (WRK-04). Ids only: the expansion re-reads the debt itself, under the transaction that
/// settles it.
/// </summary>
public sealed class PendingBackfillDeviceIdsSpec : Specification<BackfillIntent, int>
{
    public PendingBackfillDeviceIdsSpec()
    {
        Query
            .Where(intent => intent.Status == BackfillStatus.Pending)
            .Select(intent => intent.DeviceId);
    }
}
