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
public sealed class IsapiDeviceClient(IReaderConnections connections, IOptions<IsapiOptions> options)
    : IDeviceClient
{
    private readonly IsapiOptions _options = options.Value;

    /// <summary>
    /// ISA-01…ISA-06: the person is upserted, then the face replaced. A refused face leaves no
    /// PIN-only person behind (A-3); a reader that went down mid-call is left alone (ISA-06).
    /// </summary>
    public async Task<OneOf<Delivered, Rejected, Unreachable>> AddAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(enrolment);

        var client = connections.For(device);

        var person = await UpsertPersonAsync(client, enrolment, cancellationToken);
        if (person.Result is not StepResult.Ok)
            return Failure(person);

        var face = await ReplaceFaceAsync(client, enrolment, cancellationToken);
        if (face.Result is StepResult.Ok)
            return new Delivered();

        if (face.Result is not StepResult.Refused refused)
            return Failure(face);

        var compensation = await SendAsync(
            client,
            IsapiStep.PersonDelete,
            IsapiRequests.PersonDelete(enrolment.ExternalRef),
            cancellationToken
        );

        return compensation.Result is StepResult.Ok or StepResult.Absent
            ? new Rejected(refused.Reason)
            : new Rejected($"{refused.Reason}; compensation failed: {ReasonOf(compensation)}");
    }

    /// <summary>
    /// ISA-07…ISA-09: the same upsert and face replace as an add, with no compensation — the
    /// spectator was enrolled before, and a bad photograph must not lock them out.
    /// </summary>
    public async Task<OneOf<Delivered, Rejected, Unreachable>> UpdateAsync(
        DeviceEndpoint device,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(enrolment);

        var client = connections.For(device);

        var person = await UpsertPersonAsync(client, enrolment, cancellationToken);
        if (person.Result is not StepResult.Ok)
            return Failure(person);

        var face = await ReplaceFaceAsync(client, enrolment, cancellationToken);
        return face.Result is StepResult.Ok ? new Delivered() : Failure(face);
    }

    /// <summary>
    /// ISA-10, ISA-11: the face first, so a partial remove never leaves a person holding a face;
    /// nothing to delete counts as deleted.
    /// </summary>
    public async Task<OneOf<Delivered, Rejected, Unreachable>> RemoveAsync(
        DeviceEndpoint device,
        string externalRef,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(externalRef);

        var client = connections.For(device);

        var face = await SendAsync(
            client,
            IsapiStep.FaceDelete,
            IsapiRequests.FaceDelete(externalRef, _options),
            cancellationToken
        );
        if (face.Result is not (StepResult.Ok or StepResult.Absent))
            return Failure(face);

        var person = await SendAsync(
            client,
            IsapiStep.PersonDelete,
            IsapiRequests.PersonDelete(externalRef),
            cancellationToken
        );
        return person.Result is StepResult.Ok or StepResult.Absent
            ? new Delivered()
            : Failure(person);
    }

    /// <summary>ISA-02: create, and converge an existing person with a modify.</summary>
    private async Task<Exchange> UpsertPersonAsync(
        HttpClient client,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        var created = await SendAsync(
            client,
            IsapiStep.PersonCreate,
            IsapiRequests.PersonCreate(enrolment, _options),
            cancellationToken
        );
        if (created.Result is not StepResult.AlreadyExists)
            return created;

        return await SendAsync(
            client,
            IsapiStep.PersonModify,
            IsapiRequests.PersonModify(enrolment, _options),
            cancellationToken
        );
    }

    /// <summary>ISA-03: delete whatever face is there (none is fine), then write the enrolment's.</summary>
    private async Task<Exchange> ReplaceFaceAsync(
        HttpClient client,
        DeviceEnrolment enrolment,
        CancellationToken cancellationToken
    )
    {
        var deleted = await SendAsync(
            client,
            IsapiStep.FaceDelete,
            IsapiRequests.FaceDelete(enrolment.ExternalRef, _options),
            cancellationToken
        );
        if (deleted.Result is not (StepResult.Ok or StepResult.Absent))
            return deleted;

        return await SendAsync(
            client,
            IsapiStep.FaceCreate,
            IsapiRequests.FaceCreate(enrolment, _options),
            cancellationToken
        );
    }

    /// <summary>
    /// One authenticated exchange, attempted once (ISA-19). A cancelled token is never caught
    /// (ISA-20); the worker maps it to unreachable (WRK-11).
    /// </summary>
    private async Task<Exchange> SendAsync(
        HttpClient client,
        IsapiStep step,
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        using (request)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
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
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Cannot happen with Timeout = Infinite; classified rather than thrown, defensively.
                return new Exchange(step, new StepResult.ReaderDown("request timed out"), null);
            }
        }
    }

    /// <summary>The call's outcome when <paramref name="exchange"/> is where it stopped (A-4).</summary>
    private static OneOf<Delivered, Rejected, Unreachable> Failure(Exchange exchange) =>
        exchange.Result switch
        {
            StepResult.Refused refused => new Rejected(refused.Reason),
            StepResult.ReaderDown down => new Unreachable(down.Reason),
            StepResult.AuthFailed auth => new Unreachable(auth.Reason),
            _ => new Rejected(ReasonOf(exchange)),
        };

    private static string ReasonOf(Exchange exchange) =>
        exchange.Result switch
        {
            StepResult.Refused refused => refused.Reason,
            StepResult.ReaderDown down => $"{IsapiOutcomes.NameOf(exchange.Step)}: {down.Reason}",
            StepResult.AuthFailed auth => $"{IsapiOutcomes.NameOf(exchange.Step)}: {auth.Reason}",
            _ => $"{IsapiOutcomes.NameOf(exchange.Step)}: unexpected answer",
        };

    /// <summary>One step and how it ended; <paramref name="Status"/> is null when no answer came.</summary>
    private readonly record struct Exchange(IsapiStep Step, StepResult Result, int? Status);
}
