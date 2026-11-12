using System.Diagnostics;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Options;
using OneOf;

namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>
/// The real reader adapter (AD-017): each port call is a short, fixed sequence of ISAPI steps
/// (design § IsapiDeviceClient). It owns no retries, timeouts or pausing — those are the worker's
/// (AD-043) — so every step is sent once (ISA-19) and a cancelled token simply propagates
/// (ISA-20).
/// </summary>
public sealed partial class IsapiDeviceClient(
    IReaderConnections connections,
    IOptions<IsapiOptions> options,
    TimeProvider timeProvider,
    ILogger<IsapiDeviceClient> logger
) : IDeviceClient
{
    /// <summary>The source every step's client span is published on (ISA-28).</summary>
    public const string ActivitySourceName = "HikvisionReplicator.Isapi";

    /// <summary>
    /// Static because an <see cref="ActivitySource"/> is a process-wide publisher; it emits
    /// nothing unless the tracer provider names it.
    /// </summary>
    private static readonly ActivitySource Source = new(ActivitySourceName);

    private readonly IsapiOptions _options = options.Value;

    /// <summary>
    /// ISA-01…ISA-06: the person is upserted, then the face replaced. A refused face leaves no
    /// PIN-only person behind (A-3); a reader that went down mid-call is left alone (ISA-06).
    /// </summary>
    public Task<OneOf<Delivered, Rejected, Unreachable>> AddAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(enrolment);

        return ObserveAsync("Add", device, async call =>
        {
            var person = await UpsertPersonAsync(call, enrolment);
            if (person.Result is not StepResult.Ok)
                return Failure(person);

            var face = await ReplaceFaceAsync(call, enrolment);
            if (face.Result is StepResult.Ok)
                return Done;

            if (face.Result is not StepResult.Refused refused)
                return Failure(face);

            var compensation = await SendAsync(
                call,
                IsapiStep.PersonDelete,
                IsapiRequests.PersonDelete(enrolment.ExternalRef)
            );

            return compensation.Result is StepResult.Ok or StepResult.Absent
                ? new CallResult(new Rejected(refused.Reason), face)
                : new CallResult(
                    new Rejected($"{refused.Reason}; compensation failed: {ReasonOf(compensation)}"),
                    face
                );
        }, cancellationToken);
    }

    /// <summary>
    /// ISA-07…ISA-09: the same upsert and face replace as an add, with no compensation — the
    /// spectator was enrolled before, and a bad photograph must not lock them out.
    /// </summary>
    public Task<OneOf<Delivered, Rejected, Unreachable>> UpdateAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(enrolment);

        return ObserveAsync("Update", device, async call =>
        {
            var person = await UpsertPersonAsync(call, enrolment);
            if (person.Result is not StepResult.Ok)
                return Failure(person);

            var face = await ReplaceFaceAsync(call, enrolment);
            return face.Result is StepResult.Ok ? Done : Failure(face);
        }, cancellationToken);
    }

    /// <summary>
    /// ISA-10, ISA-11: the face first, so a partial remove never leaves a person holding a face;
    /// nothing to delete counts as deleted.
    /// </summary>
    public Task<OneOf<Delivered, Rejected, Unreachable>> RemoveAsync(
        DeviceEndpoint device,
        string externalRef,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(externalRef);

        return ObserveAsync("Remove", device, async call =>
        {
            var face = await SendAsync(
                call,
                IsapiStep.FaceDelete,
                IsapiRequests.FaceDelete(externalRef, _options)
            );
            if (face.Result is not (StepResult.Ok or StepResult.Absent))
                return Failure(face);

            var person = await SendAsync(call, IsapiStep.PersonDelete, IsapiRequests.PersonDelete(externalRef));
            return person.Result is StepResult.Ok or StepResult.Absent ? Done : Failure(person);
        }, cancellationToken);
    }

    /// <summary>
    /// What every port call shares: the ISA-37 hold checked before anything is sent, and exactly
    /// one log entry per call (ISA-29), including a call the worker cancelled.
    /// </summary>
    private async Task<OneOf<Delivered, Rejected, Unreachable>> ObserveAsync(
        string operation,
        DeviceEndpoint device,
        Func<Call, Task<CallResult>> steps,
        CancellationToken cancellationToken
    )
    {
        var startedAt = timeProvider.GetTimestamp();

        try
        {
            var result = connections.IsHeld(device, out var held)
                ? new CallResult(new Unreachable(held), null)
                : await steps(new Call(device, connections.For(device), cancellationToken));

            var outcome = result.Outcome.Match(_ => "Delivered", _ => "Rejected", _ => "Unreachable");
            var failedAt = result.FailedAt;
            var elapsedMs = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            LogCall(
                logger,
                outcome == "Delivered" ? LogLevel.Information : LogLevel.Warning,
                device.DeviceId,
                operation,
                outcome,
                failedAt is { } step ? IsapiOutcomes.NameOf(step.Step) : null,
                failedAt?.Status,
                (failedAt?.Result as StepResult.Refused)?.SubStatusCode,
                elapsedMs
            );

            return result.Outcome;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            var elapsedMs = timeProvider.GetElapsedTime(startedAt).TotalMilliseconds;
            LogCall(
                logger,
                LogLevel.Warning,
                device.DeviceId,
                operation,
                "Cancelled",
                null,
                null,
                null,
                elapsedMs
            );
            throw;
        }
    }

    /// <summary>ISA-02: create, and converge an existing person with a modify.</summary>
    private async Task<Exchange> UpsertPersonAsync(Call call, DeviceEnrolment enrolment)
    {
        var created = await SendAsync(
            call,
            IsapiStep.PersonCreate,
            IsapiRequests.PersonCreate(enrolment, _options)
        );
        if (created.Result is not StepResult.AlreadyExists)
            return created;

        return await SendAsync(
            call,
            IsapiStep.PersonModify,
            IsapiRequests.PersonModify(enrolment, _options)
        );
    }

    /// <summary>ISA-03: delete whatever face is there (none is fine), then write the enrolment's.</summary>
    private async Task<Exchange> ReplaceFaceAsync(Call call, DeviceEnrolment enrolment)
    {
        var deleted = await SendAsync(
            call,
            IsapiStep.FaceDelete,
            IsapiRequests.FaceDelete(enrolment.ExternalRef, _options)
        );
        if (deleted.Result is not (StepResult.Ok or StepResult.Absent))
            return deleted;

        return await SendAsync(call, IsapiStep.FaceCreate, IsapiRequests.FaceCreate(enrolment, _options));
    }

    /// <summary>
    /// One authenticated exchange, attempted once (ISA-19), in its own client span (ISA-28). A
    /// cancelled token is never caught (ISA-20); the worker maps it to unreachable (WRK-11).
    /// Refused credentials are held, so the worker's probes cannot lock the reader's account
    /// (ISA-37).
    /// </summary>
    private async Task<Exchange> SendAsync(Call call, IsapiStep step, HttpRequestMessage request)
    {
        using (request)
        {
            call.Token.ThrowIfCancellationRequested();

            // Tags are the device, the step and what the reader answered — never a URL, a header
            // or a body (ISA-24).
            using var span = Source.StartActivity(IsapiOutcomes.NameOf(step), ActivityKind.Client);
            span?.SetTag("device.id", call.Device.DeviceId);
            span?.SetTag("isapi.step", IsapiOutcomes.NameOf(step));

            var exchange = await ExchangeAsync(call, step, request);

            if (exchange.Status is { } status)
                span?.SetTag("http.response.status_code", status);
            if (exchange.Result is StepResult.Refused { SubStatusCode: { } subStatusCode })
                span?.SetTag("isapi.sub_status_code", subStatusCode);

            if (exchange.Result is StepResult.AuthFailed auth)
            {
                var until = timeProvider.GetUtcNow() + (auth.UnlockAfter ?? _options.AuthFailureHold);
                connections.Hold(call.Device, auth.Reason, until);
            }

            return exchange;
        }
    }

    private async Task<Exchange> ExchangeAsync(Call call, IsapiStep step, HttpRequestMessage request)
    {
        try
        {
            using var response = await call.Client.SendAsync(request, call.Token);
            var body = await response.Content.ReadAsStringAsync(call.Token);
            return new Exchange(
                step,
                IsapiOutcomes.Classify(step, response.StatusCode, body, _options),
                (int)response.StatusCode
            );
        }
        catch (HttpRequestException exception)
        {
            return new Exchange(step, IsapiOutcomes.Classify(step, exception), null);
        }
        catch (OperationCanceledException) when (!call.Token.IsCancellationRequested)
        {
            // Cannot happen with Timeout = Infinite; classified rather than thrown, defensively.
            return new Exchange(step, new StepResult.ReaderDown("request timed out"), null);
        }
    }

    private static readonly CallResult Done = new(new Delivered(), null);

    /// <summary>The call's outcome when <paramref name="exchange"/> is where it stopped (A-4).</summary>
    private static CallResult Failure(Exchange exchange) =>
        new(
            exchange.Result switch
            {
                StepResult.Refused refused => new Rejected(refused.Reason),
                StepResult.ReaderDown down => new Unreachable(down.Reason),
                StepResult.AuthFailed auth => new Unreachable(auth.Reason),
                _ => new Rejected(ReasonOf(exchange)),
            },
            exchange
        );

    private static string ReasonOf(Exchange exchange) =>
        exchange.Result switch
        {
            StepResult.Refused refused => refused.Reason,
            StepResult.ReaderDown down => $"{IsapiOutcomes.NameOf(exchange.Step)}: {down.Reason}",
            StepResult.AuthFailed auth => $"{IsapiOutcomes.NameOf(exchange.Step)}: {auth.Reason}",
            _ => $"{IsapiOutcomes.NameOf(exchange.Step)}: unexpected answer",
        };

    /// <summary>ISA-29: one entry per call; Information when delivered, Warning otherwise.</summary>
    [LoggerMessage(
        EventId = 40,
        Message = "ISAPI {Operation} on device {DeviceId} ended {Outcome}; failing step {FailingStep}, HTTP {HttpStatus}, sub-status {SubStatusCode}, {ElapsedMs} ms."
    )]
    private static partial void LogCall(
        ILogger logger,
        LogLevel level,
        int deviceId,
        string operation,
        string outcome,
        string? failingStep,
        int? httpStatus,
        string? subStatusCode,
        double elapsedMs
    );

    /// <summary>The reader, its client and the worker's token, for every step of one port call.</summary>
    private readonly record struct Call(DeviceEndpoint Device, HttpClient Client, CancellationToken Token);

    /// <summary>One step and how it ended; <paramref name="Status"/> is null when no answer came.</summary>
    private readonly record struct Exchange(IsapiStep Step, StepResult Result, int? Status);

    /// <summary>A call's outcome and the step it stopped at, if it did not deliver.</summary>
    private readonly record struct CallResult(
        OneOf<Delivered, Rejected, Unreachable> Outcome,
        Exchange? FailedAt
    );
}
