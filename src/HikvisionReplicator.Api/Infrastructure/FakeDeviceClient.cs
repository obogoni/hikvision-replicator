using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>One call the fake fleet received, in the order it arrived.</summary>
public sealed record DeviceCall(
    int DeviceId,
    ReplicationOperation Operation,
    string ExternalRef,
    DateTimeOffset At,
    DeviceEndpoint Endpoint,
    DeviceEnrolment? Enrolment
);

/// <summary>
/// An in-memory reader fleet behind the real port (AD-017), for Phase 2 and every worker test.
/// <para>
/// <b>Registered only when <c>DeviceClient:Adapter</c> is <c>Fake</c>.</b> A fake that always
/// succeeds would mark every row <c>Succeeded</c> while no turnstile knows anybody, so it is
/// never a default — production fails to start until feature 5 ships a real adapter.
/// </para>
/// <para>
/// Scripted rather than random, so a test chooses exactly which reader is slow, offline or
/// refusing. Latency waits on the injected clock and the call's token, so the worker's timeout
/// cancels it exactly as it would a real socket (WRK-11). Every call is logged — answered or
/// not — and the highest number of calls ever overlapping on one reader is kept, which is what
/// proves WRK-06 from the reader's side.
/// </para>
/// </summary>
public sealed class FakeDeviceClient(TimeProvider timeProvider) : IDeviceClient
{
    private readonly Lock _gate = new();
    private readonly List<DeviceCall> _calls = [];
    private readonly Dictionary<int, TimeSpan> _latencyByDevice = [];
    private readonly HashSet<int> _unreachable = [];
    private readonly Dictionary<int, Dictionary<string, DeviceEnrolment>> _enrolled = [];
    private readonly Dictionary<int, int> _inFlight = [];
    private readonly Dictionary<int, int> _maxInFlight = [];
    private TimeSpan _latency = TimeSpan.Zero;
    private Func<int, string, bool> _rejectWhen = (_, _) => false;

    /// <summary>Every call so far, in arrival order.</summary>
    public IReadOnlyList<DeviceCall> Calls
    {
        get
        {
            lock (_gate)
                return [.. _calls];
        }
    }

    /// <summary>How long every reader takes to answer, unless one has its own.</summary>
    public void SetLatency(TimeSpan latency)
    {
        lock (_gate)
            _latency = latency;
    }

    /// <summary>How long one reader takes to answer — the slow reader of WRK-07.</summary>
    public void SetLatency(int deviceId, TimeSpan latency)
    {
        lock (_gate)
            _latencyByDevice[deviceId] = latency;
    }

    /// <summary>Takes a reader off the network, or puts it back (WRK-11…WRK-14).</summary>
    public void SetUnreachable(int deviceId, bool unreachable)
    {
        lock (_gate)
        {
            if (unreachable)
                _unreachable.Add(deviceId);
            else
                _unreachable.Remove(deviceId);
        }
    }

    /// <summary>Which (reader, external reference) pairs the reader refuses (WRK-16).</summary>
    public void RejectWhen(Func<int, string, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        lock (_gate)
            _rejectWhen = predicate;
    }

    /// <summary>What one reader holds now, by external reference.</summary>
    public IReadOnlyDictionary<string, DeviceEnrolment> Enrolled(int deviceId)
    {
        lock (_gate)
        {
            return _enrolled.TryGetValue(deviceId, out var held)
                ? new Dictionary<string, DeviceEnrolment>(held)
                : [];
        }
    }

    /// <summary>The most calls ever in flight at once against one reader (WRK-06).</summary>
    public int MaxConcurrentCalls(int deviceId)
    {
        lock (_gate)
            return _maxInFlight.GetValueOrDefault(deviceId);
    }

    /// <summary>Back to an empty, healthy, instant fleet — for a harness shared across tests.</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _calls.Clear();
            _latencyByDevice.Clear();
            _unreachable.Clear();
            _enrolled.Clear();
            _inFlight.Clear();
            _maxInFlight.Clear();
            _latency = TimeSpan.Zero;
            _rejectWhen = (_, _) => false;
        }
    }

    public Task<OneOf<Delivered, Rejected, Unreachable>> AddAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(enrolment);
        return CallAsync(
            device,
            ReplicationOperation.Add,
            enrolment.ExternalRef,
            enrolment,
            cancellationToken
        );
    }

    public Task<OneOf<Delivered, Rejected, Unreachable>> UpdateAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(enrolment);
        return CallAsync(
            device,
            ReplicationOperation.Update,
            enrolment.ExternalRef,
            enrolment,
            cancellationToken
        );
    }

    public Task<OneOf<Delivered, Rejected, Unreachable>> RemoveAsync(
        DeviceEndpoint device,
        string externalRef,
        CancellationToken cancellationToken
    ) => CallAsync(device, ReplicationOperation.Remove, externalRef, null, cancellationToken);

    private async Task<OneOf<Delivered, Rejected, Unreachable>> CallAsync(
        DeviceEndpoint device,
        ReplicationOperation operation,
        string externalRef,
        DeviceEnrolment? enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);

        TimeSpan latency;
        lock (_gate)
        {
            _calls.Add(
                new DeviceCall(
                    device.DeviceId,
                    operation,
                    externalRef,
                    timeProvider.GetUtcNow(),
                    device,
                    enrolment
                )
            );

            var inFlight = _inFlight.GetValueOrDefault(device.DeviceId) + 1;
            _inFlight[device.DeviceId] = inFlight;
            _maxInFlight[device.DeviceId] = Math.Max(
                inFlight,
                _maxInFlight.GetValueOrDefault(device.DeviceId)
            );

            latency = _latencyByDevice.GetValueOrDefault(device.DeviceId, _latency);
        }

        try
        {
            if (latency > TimeSpan.Zero)
                await Task.Delay(latency, timeProvider, cancellationToken);

            lock (_gate)
            {
                if (_unreachable.Contains(device.DeviceId))
                    return new Unreachable($"Device {device.DeviceId} did not answer.");

                if (_rejectWhen(device.DeviceId, externalRef))
                    return new Rejected($"Device {device.DeviceId} refused {externalRef}.");

                var held = _enrolled.TryGetValue(device.DeviceId, out var existing)
                    ? existing
                    : _enrolled[device.DeviceId] = [];

                if (enrolment is null)
                    held.Remove(externalRef);
                else
                    held[externalRef] = enrolment;

                return new Delivered();
            }
        }
        finally
        {
            lock (_gate)
                _inFlight[device.DeviceId]--;
        }
    }
}
