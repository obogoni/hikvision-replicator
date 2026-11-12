using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.Logging;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// A client span per step and one log entry per call (ISA-28, ISA-29), with no secret in either
/// (ISA-24). Spans are filtered on this class's device id, since a listener is process-wide.
/// </summary>
public partial class IsapiDeviceClientTests
{
    private sealed class StepSpans : IDisposable
    {
        private readonly ActivityListener _listener;

        public List<Activity> Stopped { get; } = [];

        public StepSpans()
        {
            _listener = new ActivityListener
            {
                ShouldListenTo = source => source.Name == "HikvisionReplicator.Isapi",
                Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                    ActivitySamplingResult.AllDataAndRecorded,
                ActivityStopped = activity =>
                {
                    if (activity.GetTagItem("device.id") is 7)
                        Stopped.Add(activity);
                },
            };
            ActivitySource.AddActivityListener(_listener);
        }

        public void Dispose() => _listener.Dispose();
    }

    private void EachRequestTakes(TimeSpan duration) =>
        _reader.OnEachRequest = () => _clock.Advance(duration);

    private CapturingLogger<Api.Infrastructure.Isapi.IsapiDeviceClient>.Entry SingleEntry() =>
        Assert.Single(_logger.Entries);

    // ─── Spans (ISA-28) ───

    [Fact]
    public async Task Every_step_is_a_client_span_tagged_with_the_device_the_step_and_the_status()
    {
        using var spans = new StepSpans();

        await AddAsync();

        Assert.Equal(["person", "face-delete", "face"], spans.Stopped.Select(span => span.DisplayName));
        Assert.All(
            spans.Stopped,
            span =>
            {
                Assert.Equal(ActivityKind.Client, span.Kind);
                Assert.Equal(7, span.GetTagItem("device.id"));
                Assert.Equal(span.DisplayName, span.GetTagItem("isapi.step"));
                Assert.Equal(200, span.GetTagItem("http.response.status_code"));
                Assert.Null(span.GetTagItem("isapi.sub_status_code"));
            }
        );
    }

    [Fact]
    public async Task A_refused_step_carries_the_readers_sub_status_and_the_compensation_is_its_own_span()
    {
        using var spans = new StepSpans();
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);

        await AddAsync();

        Assert.Equal(
            ["person", "face-delete", "face", "person-delete"],
            spans.Stopped.Select(span => span.DisplayName)
        );
        var face = spans.Stopped[2];
        Assert.Equal(400, face.GetTagItem("http.response.status_code"));
        Assert.Equal("faceQualityTooLow", face.GetTagItem("isapi.sub_status_code"));
        Assert.Equal("person-delete", spans.Stopped[3].GetTagItem("isapi.step"));
    }

    // ─── One log entry per call (ISA-29) ───

    [Fact]
    public async Task A_delivered_call_is_logged_once_at_information_with_its_duration()
    {
        EachRequestTakes(TimeSpan.FromMilliseconds(250));

        await AddAsync();

        var entry = SingleEntry();
        Assert.Equal(LogLevel.Information, entry.Level);
        Assert.Equal(7, entry.Fields["DeviceId"]);
        Assert.Equal("Add", entry.Fields["Operation"]);
        Assert.Equal("Delivered", entry.Fields["Outcome"]);
        Assert.Null(entry.Fields["FailingStep"]);
        Assert.Null(entry.Fields["HttpStatus"]);
        Assert.Null(entry.Fields["SubStatusCode"]);
        Assert.Equal(750d, entry.Fields["ElapsedMs"]);
    }

    [Fact]
    public async Task A_rejected_call_is_logged_once_at_warning_naming_the_failing_step()
    {
        EachRequestTakes(TimeSpan.FromMilliseconds(250));
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);

        await AddAsync();

        var entry = SingleEntry();
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(7, entry.Fields["DeviceId"]);
        Assert.Equal("Add", entry.Fields["Operation"]);
        Assert.Equal("Rejected", entry.Fields["Outcome"]);
        Assert.Equal("face", entry.Fields["FailingStep"]);
        Assert.Equal(400, entry.Fields["HttpStatus"]);
        Assert.Equal("faceQualityTooLow", entry.Fields["SubStatusCode"]);
        Assert.Equal(1000d, entry.Fields["ElapsedMs"]);
    }

    [Fact]
    public async Task An_unreachable_call_is_logged_once_at_warning_and_so_is_a_held_one()
    {
        _reader.Answer(RecordPath, HttpStatusCode.Unauthorized, UnauthorizedBody);

        await UpdateAsync();
        await RemoveAsync();

        Assert.Equal(2, _logger.Entries.Count);
        var refused = _logger.Entries[0];
        Assert.Equal(LogLevel.Warning, refused.Level);
        Assert.Equal("Update", refused.Fields["Operation"]);
        Assert.Equal("Unreachable", refused.Fields["Outcome"]);
        Assert.Equal("person", refused.Fields["FailingStep"]);
        Assert.Equal(401, refused.Fields["HttpStatus"]);
        var held = _logger.Entries[1];
        Assert.Equal(LogLevel.Warning, held.Level);
        Assert.Equal("Remove", held.Fields["Operation"]);
        Assert.Equal("Unreachable", held.Fields["Outcome"]);
        Assert.Null(held.Fields["FailingStep"]);
        Assert.Null(held.Fields["HttpStatus"]);
    }

    [Fact]
    public async Task A_cancelled_call_is_logged_once_at_warning()
    {
        using var worker = new CancellationTokenSource();
        _reader.CancelDuring(FacePath, worker);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AddAsync(worker.Token));

        var entry = SingleEntry();
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal("Add", entry.Fields["Operation"]);
        Assert.Equal("Cancelled", entry.Fields["Outcome"]);
    }

    // ─── No secret in a span, a log entry or a reason (ISA-24) ───

    [Fact]
    public async Task No_password_pin_or_digest_value_reaches_a_span_a_log_entry_or_a_reason()
    {
        using var spans = new StepSpans();
        const string leakyRefusal =
            """{"statusCode":6,"subStatusCode":"faceQualityTooLow","errorCode":1610637355,"errorMsg":"face quality too low","echo":"Front-Pa55 908172","Authorization":"Digest username=\"admin\", response=\"9f1c\""}""";
        var reasons = new List<string>();

        reasons.Add((await AddAsync()).Value.ToString()!);
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, leakyRefusal);
        reasons.Add((await UpdateAsync()).Value.ToString()!);
        _reader.Answer(
            RecordPath,
            HttpStatusCode.Unauthorized,
            UnauthorizedBody,
            challenge: "Digest realm=\"DS-K1T342\", qop=\"auth\", nonce=\"Front-Pa55\""
        );
        reasons.Add((await AddAsync()).Value.ToString()!);

        var observed = reasons
            .Concat(_logger.Entries.Select(entry => entry.Message))
            .Concat(_logger.Entries.SelectMany(entry => entry.Fields.Values.Select(value => $"{value}")))
            .Concat(spans.Stopped.Select(span => span.DisplayName))
            .Concat(spans.Stopped.SelectMany(span => span.TagObjects.Select(tag => $"{tag.Key}={tag.Value}")))
            .ToList();

        Assert.Equal(3, _logger.Entries.Count);
        Assert.NotEmpty(spans.Stopped);
        Assert.All(
            observed,
            text =>
            {
                Assert.DoesNotContain("Front-Pa55", text, StringComparison.Ordinal);
                Assert.DoesNotContain("908172", text, StringComparison.Ordinal);
                Assert.DoesNotContain("Digest ", text, StringComparison.Ordinal);
            }
        );
    }
}
