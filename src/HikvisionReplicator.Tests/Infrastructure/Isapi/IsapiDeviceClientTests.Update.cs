using System.Net;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>Updating a spectator (ISA-07…ISA-09).</summary>
public partial class IsapiDeviceClientTests
{
    private Task<OneOf<Delivered, Rejected, Unreachable>> UpdateAsync() =>
        _client.UpdateAsync(FrontGate, Spectator, CancellationToken.None);

    [Fact]
    public async Task Updating_a_spectator_on_the_reader_modifies_the_person_and_replaces_the_face()
    {
        _reader.Answer(RecordPath, HttpStatusCode.BadRequest, AlreadyExistsBody);

        var outcome = await UpdateAsync();

        AssertDelivered(outcome);
        AssertSent(PersonCreate, PersonModify, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task Updating_a_spectator_the_reader_lacks_creates_the_person_and_the_face()
    {
        _reader.Answer(FaceDeletePath, HttpStatusCode.BadRequest, AbsentBody);

        var outcome = await UpdateAsync();

        AssertDelivered(outcome);
        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task A_refused_new_face_is_rejected_and_the_person_is_kept()
    {
        _reader.Answer(RecordPath, HttpStatusCode.BadRequest, AlreadyExistsBody);
        _reader.Answer(FacePath, HttpStatusCode.BadRequest, FaceRefusedBody);

        var outcome = await UpdateAsync();

        AssertRejected(outcome, "face: faceQualityTooLow (1610637355) face quality too low");
        AssertSent(PersonCreate, PersonModify, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task A_refused_face_removal_on_update_is_rejected_and_the_person_is_kept()
    {
        _reader.Answer(FaceDeletePath, HttpStatusCode.BadRequest, NoPermissionBody);

        var outcome = await UpdateAsync();

        AssertRejected(outcome, "face-delete: noPermission (1073741826)");
        AssertSent(PersonCreate, FaceDelete);
    }

    [Fact]
    public async Task A_refused_person_on_update_is_rejected_and_no_face_is_sent()
    {
        _reader.Answer(RecordPath, HttpStatusCode.BadRequest, AlreadyExistsBody);
        _reader.Answer(ModifyPath, HttpStatusCode.BadRequest, NoPermissionBody);

        var outcome = await UpdateAsync();

        AssertRejected(outcome, "person-modify: noPermission (1073741826)");
        AssertSent(PersonCreate, PersonModify);
    }

    [Fact]
    public async Task A_reader_that_drops_during_an_update_is_unreachable()
    {
        _reader.Fail(FacePath, HttpRequestError.ConnectionError);

        var outcome = await UpdateAsync();

        AssertUnreachable(outcome, "ConnectionError");
        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }
}
