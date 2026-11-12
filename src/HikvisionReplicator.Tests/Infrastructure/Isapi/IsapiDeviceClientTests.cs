using System.Net;
using HikvisionReplicator.Api.Infrastructure.Isapi;
using HikvisionReplicator.Api.Shared;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OneOf;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// The adapter's step sequencing over a scripted reader (ISA-01…ISA-11, ISA-19, ISA-20, ISA-37):
/// every test asserts the exact requests sent, in order, which also proves no step is sent twice
/// (ISA-19). Paths, reasons and the key are literals; the key for <c>TICKET-0001</c> is
/// <c>b43a95a2c19a97b6289861e47299284f</c>, computed with <c>sha256sum</c>.
/// </summary>
public partial class IsapiDeviceClientTests : IDisposable
{
    private const string Key = "b43a95a2c19a97b6289861e47299284f";

    // The paths the reader is scripted on…
    private const string RecordPath = "/ISAPI/AccessControl/UserInfo/Record";
    private const string ModifyPath = "/ISAPI/AccessControl/UserInfo/Modify";
    private const string PersonDeletePath = "/ISAPI/AccessControl/UserInfo/Delete";
    private const string FaceDeletePath = "/ISAPI/Intelligent/FDLib/FDSearch/Delete";
    private const string FacePath = "/ISAPI/Intelligent/FDLib/FaceDataRecord";

    // …and the requests it receives.
    private const string PersonCreate = "POST /ISAPI/AccessControl/UserInfo/Record?format=json";
    private const string PersonModify = "PUT /ISAPI/AccessControl/UserInfo/Modify?format=json";
    private const string PersonDelete = "PUT /ISAPI/AccessControl/UserInfo/Delete?format=json";
    private const string FaceDelete =
        "PUT /ISAPI/Intelligent/FDLib/FDSearch/Delete?format=json&FDID=1&faceLibType=blackFD";
    private const string FaceCreate = "POST /ISAPI/Intelligent/FDLib/FaceDataRecord?format=json";

    private const string AlreadyExistsBody =
        """{"statusCode":6,"statusString":"Invalid Content","subStatusCode":"deviceUserAlreadyExist"}""";
    private const string AbsentBody =
        """{"statusCode":6,"statusString":"Invalid Content","subStatusCode":"deviceUserNotExist"}""";
    private const string FaceRefusedBody =
        """{"statusCode":6,"statusString":"Invalid Content","subStatusCode":"faceQualityTooLow","errorCode":1610637355,"errorMsg":"face quality too low"}""";
    private const string BusyBody =
        """{"statusCode":4,"statusString":"Invalid Operation","subStatusCode":"deviceBusy"}""";
    private const string NoPermissionBody =
        """{"statusCode":4,"statusString":"Invalid Operation","subStatusCode":"noPermission","errorCode":1073741826}""";

    private static readonly DeviceEndpoint FrontGate = new(7, "10.0.0.5", 443, "admin", "Front-Pa55");

    private static readonly DeviceEnrolment Spectator = new(
        "TICKET-0001",
        "Ana Souza",
        "908172",
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0xFF, 0xD9 }
    );

    private readonly FakeTimeProvider _clock = new(
        new DateTimeOffset(2026, 11, 12, 18, 0, 0, TimeSpan.Zero)
    );
    private readonly ScriptedReader _reader = new();
    private readonly IsapiOptions _options = new() { AbsentSubStatusCodes = ["deviceUserNotExist"] };
    private readonly ReaderConnections _connections;
    private readonly IsapiDeviceClient _client;

    public IsapiDeviceClientTests()
    {
        _connections = new ReaderConnections(_clock, _ => _reader);
        _client = new IsapiDeviceClient(_connections, Options.Create(_options));
    }

    public void Dispose()
    {
        _connections.Dispose();
        _reader.Dispose();
        GC.SuppressFinalize(this);
    }

    private void AssertSent(params string[] expected) => Assert.Equal(expected, _reader.Sent);

    private static Delivered AssertDelivered(OneOf<Delivered, Rejected, Unreachable> outcome) =>
        Assert.IsType<Delivered>(outcome.Value);

    private static void AssertRejected(OneOf<Delivered, Rejected, Unreachable> outcome, string reason) =>
        Assert.Equal(new Rejected(reason), outcome.Value);

    private static void AssertUnreachable(
        OneOf<Delivered, Rejected, Unreachable> outcome,
        string reason
    ) => Assert.Equal(new Unreachable(reason), outcome.Value);
}
