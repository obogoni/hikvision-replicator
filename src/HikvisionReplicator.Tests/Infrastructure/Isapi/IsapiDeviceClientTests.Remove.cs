using System.Net;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;
using OneOf;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>Removing a spectator (ISA-10, ISA-11).</summary>
public partial class IsapiDeviceClientTests
{
    private Task<OneOf<Delivered, Rejected, Unreachable>> RemoveAsync() =>
        _client.RemoveAsync(FrontGate, "TICKET-0001", CancellationToken.None);

    [Fact]
    public async Task Removing_a_spectator_deletes_the_face_then_the_person()
    {
        var outcome = await RemoveAsync();

        AssertDelivered(outcome);
        AssertSent(FaceDelete, PersonDelete);
        using var face = JsonDocument.Parse(_reader.Bodies[0]);
        Assert.Equal(Key, Assert.Single(face.RootElement.GetProperty("FPID").EnumerateArray()).GetProperty("value").GetString());
        using var person = JsonDocument.Parse(_reader.Bodies[1]);
        var removed = Assert.Single(
            person.RootElement.GetProperty("UserInfoDelCond").GetProperty("EmployeeNoList").EnumerateArray()
        );
        Assert.Equal(Key, removed.GetProperty("employeeNo").GetString());
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task Removing_what_is_already_absent_is_delivered(bool faceAbsent, bool personAbsent)
    {
        if (faceAbsent)
            _reader.Answer(FaceDeletePath, HttpStatusCode.BadRequest, AbsentBody);
        if (personAbsent)
            _reader.Answer(PersonDeletePath, HttpStatusCode.BadRequest, AbsentBody);

        var outcome = await RemoveAsync();

        AssertDelivered(outcome);
        AssertSent(FaceDelete, PersonDelete);
    }

    [Fact]
    public async Task A_refused_face_removal_is_rejected_and_the_person_is_not_touched()
    {
        _reader.Answer(FaceDeletePath, HttpStatusCode.BadRequest, NoPermissionBody);

        var outcome = await RemoveAsync();

        AssertRejected(outcome, "face-delete: noPermission (1073741826)");
        AssertSent(FaceDelete);
    }

    [Fact]
    public async Task A_refused_person_removal_is_rejected()
    {
        _reader.Answer(PersonDeletePath, HttpStatusCode.BadRequest, BusyBody);

        var outcome = await RemoveAsync();

        AssertRejected(outcome, "person-delete: deviceBusy");
        AssertSent(FaceDelete, PersonDelete);
    }

    [Fact]
    public async Task A_reader_that_drops_during_a_remove_is_unreachable()
    {
        _reader.Fail(PersonDeletePath, HttpRequestError.ConnectionError);

        var outcome = await RemoveAsync();

        AssertUnreachable(outcome, "ConnectionError");
        AssertSent(FaceDelete, PersonDelete);
    }
}
