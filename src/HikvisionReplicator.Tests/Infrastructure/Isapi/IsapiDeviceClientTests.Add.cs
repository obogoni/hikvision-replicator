using System.Net;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>Enrolling a spectator (ISA-01…ISA-06, ISA-19, ISA-20).</summary>
public partial class IsapiDeviceClientTests
{
    private Task<OneOf<Delivered, Rejected, Unreachable>> AddAsync(
        CancellationToken cancellationToken = default
    ) => _client.AddAsync(FrontGate, Spectator, cancellationToken);

    // ─── Delivered ───

    [Fact]
    public async Task A_new_spectator_is_written_as_a_person_then_given_the_face()
    {
        var outcome = await AddAsync();

        AssertDelivered(outcome);
        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task A_spectator_already_on_the_reader_is_converged_and_delivered()
    {
        _reader.Answer(RecordPath, HttpStatusCode.BadRequest, AlreadyExistsBody);

        var outcome = await AddAsync();

        AssertDelivered(outcome);
        AssertSent(PersonCreate, PersonModify, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task A_spectator_with_no_face_to_replace_is_still_delivered()
    {
        _reader.Answer(FaceDeletePath, HttpStatusCode.BadRequest, AbsentBody);

        var outcome = await AddAsync();

        AssertDelivered(outcome);
        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }

    // ─── A refused face is compensated (ISA-04, ISA-05) ───

    [Fact]
    public async Task A_refused_face_removes_the_person_and_rejects_naming_the_face_step()
    {
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);

        var outcome = await AddAsync();

        AssertRejected(outcome, "face: faceQualityTooLow (1610637355) face quality too low");
        AssertSent(PersonCreate, FaceDelete, FaceCreate, PersonDelete);
        using var compensation = JsonDocument.Parse(_reader.Bodies[3]);
        var removed = Assert.Single(
            compensation.RootElement.GetProperty("UserInfoDelCond").GetProperty("EmployeeNoList").EnumerateArray()
        );
        Assert.Equal(Key, removed.GetProperty("employeeNo").GetString());
    }

    [Fact]
    public async Task A_refused_face_removal_also_removes_the_person()
    {
        _reader.Answer(FaceDeletePath, HttpStatusCode.BadRequest, NoPermissionBody);

        var outcome = await AddAsync();

        AssertRejected(outcome, "face-delete: noPermission (1073741826)");
        AssertSent(PersonCreate, FaceDelete, PersonDelete);
    }

    [Fact]
    public async Task A_compensation_the_reader_finds_nothing_to_delete_for_still_rejects_cleanly()
    {
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);
        _reader.Answer(PersonDeletePath, HttpStatusCode.BadRequest, AbsentBody);

        var outcome = await AddAsync();

        AssertRejected(outcome, "face: faceQualityTooLow (1610637355) face quality too low");
        AssertSent(PersonCreate, FaceDelete, FaceCreate, PersonDelete);
    }

    [Fact]
    public async Task A_refused_compensation_rejects_saying_compensation_failed()
    {
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);
        _reader.Answer(PersonDeletePath, HttpStatusCode.BadRequest, BusyBody);

        var outcome = await AddAsync();

        AssertRejected(
            outcome,
            "face: faceQualityTooLow (1610637355) face quality too low; compensation failed: person-delete: deviceBusy"
        );
        AssertSent(PersonCreate, FaceDelete, FaceCreate, PersonDelete);
    }

    [Fact]
    public async Task A_compensation_that_cannot_reach_the_reader_rejects_saying_compensation_failed()
    {
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);
        _reader.Fail(PersonDeletePath, HttpRequestError.ConnectionError);

        var outcome = await AddAsync();

        AssertRejected(
            outcome,
            "face: faceQualityTooLow (1610637355) face quality too low; compensation failed: person-delete: ConnectionError"
        );
        AssertSent(PersonCreate, FaceDelete, FaceCreate, PersonDelete);
    }

    // ─── A refused person stops the call ───

    [Fact]
    public async Task A_refused_person_is_rejected_and_no_face_is_sent()
    {
        _reader.Answer(RecordPath, HttpStatusCode.BadRequest, NoPermissionBody);

        var outcome = await AddAsync();

        AssertRejected(outcome, "person: noPermission (1073741826)");
        AssertSent(PersonCreate);
    }

    [Fact]
    public async Task A_refused_modify_is_rejected_and_no_face_is_sent()
    {
        _reader.Answer(RecordPath, HttpStatusCode.BadRequest, AlreadyExistsBody);
        _reader.Answer(ModifyPath, HttpStatusCode.BadRequest, NoPermissionBody);

        var outcome = await AddAsync();

        AssertRejected(outcome, "person-modify: noPermission (1073741826)");
        AssertSent(PersonCreate, PersonModify);
    }

    // ─── A reader that goes down is left alone (ISA-06) ───

    public static TheoryData<string, string[]> EveryAddStep =>
        new()
        {
            { RecordPath, [PersonCreate] },
            { ModifyPath, [PersonCreate, PersonModify] },
            { FaceDeletePath, [PersonCreate, FaceDelete] },
            { FacePath, [PersonCreate, FaceDelete, FaceCreate] },
        };

    private void ReachStep(string path)
    {
        if (path == ModifyPath)
            _reader.Answer(RecordPath, HttpStatusCode.BadRequest, AlreadyExistsBody);
    }

    [Theory]
    [MemberData(nameof(EveryAddStep))]
    public async Task A_reader_that_drops_at_any_step_is_unreachable_and_nothing_is_compensated(
        string path,
        string[] expected
    )
    {
        ReachStep(path);
        _reader.Fail(path, HttpRequestError.ConnectionError);

        var outcome = await AddAsync();

        AssertUnreachable(outcome, "ConnectionError");
        AssertSent(expected);
    }

    [Fact]
    public async Task A_face_step_answering_5xx_is_unreachable_and_nothing_is_compensated()
    {
        _reader.Answer(FacePath, HttpStatusCode.ServiceUnavailable, "<html>busy</html>");

        var outcome = await AddAsync();

        AssertUnreachable(outcome, "HTTP 503");
        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task A_request_abandoned_while_the_workers_token_is_live_is_unreachable()
    {
        _reader.Abandon(FacePath);

        var outcome = await AddAsync();

        AssertUnreachable(outcome, "request timed out");
        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }

    // ─── Cancellation propagates (ISA-20) ───

    [Theory]
    [MemberData(nameof(EveryAddStep))]
    public async Task A_call_cancelled_at_any_step_stops_there_and_compensates_nothing(
        string path,
        string[] expected
    )
    {
        using var worker = new CancellationTokenSource();
        ReachStep(path);
        _reader.CancelDuring(path, worker);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AddAsync(worker.Token));

        AssertSent(expected);
    }

    [Fact]
    public async Task A_call_cancelled_before_it_starts_sends_nothing()
    {
        using var worker = new CancellationTokenSource();
        await worker.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => AddAsync(worker.Token));

        AssertSent();
    }
}
