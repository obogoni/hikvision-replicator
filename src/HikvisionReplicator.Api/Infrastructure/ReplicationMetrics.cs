using System.Diagnostics.Metrics;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// What the queue publishes about itself (REP-35…REP-38). Nothing drains yet, so the
/// operational value lands with the runner — but the write path is instrumented now, because
/// instrumenting it later means re-opening it.
/// <para>
/// <b>The meter must be named in <c>Program.cs</c>'s <c>AddMeter</c></b> or every instrument
/// below records into nothing while a test that installs its own listener still passes
/// (L-037, REP-39). That is not a theoretical failure here: it already shipped once.
/// </para>
/// <para>
/// Counting happens where the rows are <em>staged</em>, which is before the save that commits
/// them (AD-041). A write that then loses the pending-index race has been counted for work it
/// did not leave behind — the queue's own tables remain the authority on what is owed; these
/// are rate signals.
/// </para>
/// </summary>
public sealed class ReplicationMetrics
{
    /// <summary>The meter every instrument below is published on (REP-39).</summary>
    public const string MeterName = "HikvisionReplicator.Replication";

    public const string EnqueuedMetricName = "replication.enqueued";

    public const string SupersededMetricName = "replication.superseded";

    public const string CapacityExceededMetricName = "replication.capacity_exceeded";

    public const string FanOutSizeMetricName = "replication.fanout.size";

    public const string OperationTag = "operation";

    public const string LaneTag = "lane";

    public const string DeviceTag = "device";

    private readonly Counter<long> _enqueued;
    private readonly Counter<long> _superseded;
    private readonly Counter<long> _capacityExceeded;
    private readonly Histogram<int> _fanOutSize;

    public ReplicationMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);

        // Through the factory rather than a bare `new Meter(...)`, as the normalizer does:
        // the factory owns the meter's lifetime and scopes it to this container.
        var meter = meterFactory.Create(MeterName);

        _enqueued = meter.CreateCounter<long>(
            EnqueuedMetricName,
            unit: "{replication}",
            description: "Pieces of outstanding work put on the queue."
        );
        _superseded = meter.CreateCounter<long>(
            SupersededMetricName,
            unit: "{replication}",
            description: "Pending work replaced by a later intent for the same spectator."
        );
        _capacityExceeded = meter.CreateCounter<long>(
            CapacityExceededMetricName,
            unit: "{signal}",
            description: "Times the active roster was found above a registered reader's ceiling."
        );
        _fanOutSize = meter.CreateHistogram<int>(
            FanOutSizeMetricName,
            unit: "{replication}",
            description: "How many readers one spectator write owed work to."
        );
    }

    /// <summary>
    /// REP-35. Tagged by operation and lane — the two axes that decide what the runner does
    /// with a row, and both closed sets, so neither can inflate cardinality.
    /// </summary>
    public void Enqueued(ReplicationOperation operation, ReplicationLane lane) =>
        _enqueued.Add(
            1,
            new KeyValuePair<string, object?>(OperationTag, operation.ToString()),
            new KeyValuePair<string, object?>(LaneTag, lane.ToString())
        );

    /// <summary>REP-36. Untagged: supersession is a property of the queue, not of a reader.</summary>
    public void Superseded() => _superseded.Add(1);

    /// <summary>
    /// REP-37. Tagged by the reader and <em>only</em> the reader: the active-user count the
    /// warning log carries is unbounded cardinality and would be a bill, not a signal.
    /// </summary>
    public void CapacityExceeded(int deviceId) =>
        _capacityExceeded.Add(1, new KeyValuePair<string, object?>(DeviceTag, deviceId));

    /// <summary>
    /// REP-38. One record per spectator write, so the per-device write cost of AD-038's
    /// twenty-reader envelope is observable rather than assumed.
    /// </summary>
    public void FanOut(int size) => _fanOutSize.Record(size);
}
