using System.Net;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// Refused credentials pause the reader and are held, so the worker's probes cannot lock the
/// reader's account (ISA-15, ISA-37). The default hold is 30 minutes.
/// </summary>
public partial class IsapiDeviceClientTests
{
    private const string UnauthorizedBody =
        """{"userCheck":{"statusValue":401,"statusString":"Unauthorized","isActivated":true,"lockStatus":"unlock","unlockTime":0,"retryLoginTime":4}}""";
    private const string LockedBody =
        """{"userCheck":{"statusValue":401,"statusString":"Unauthorized","isActivated":true,"lockStatus":"lock","unlockTime":120,"retryLoginTime":0}}""";

    [Fact]
    public async Task Refused_credentials_are_unreachable_and_not_tried_again()
    {
        _reader.Answer(RecordPath, HttpStatusCode.Unauthorized, UnauthorizedBody);

        var first = await AddAsync();
        var second = await AddAsync();
        var third = await UpdateAsync();
        var fourth = await RemoveAsync();

        AssertUnreachable(first, "authentication failed");
        AssertUnreachable(second, "authentication failed");
        AssertUnreachable(third, "authentication failed");
        AssertUnreachable(fourth, "authentication failed");
        AssertSent(PersonCreate);
    }

    [Fact]
    public async Task Refused_credentials_are_held_for_the_configured_hold_and_tried_once_it_lapses()
    {
        _reader.Answer(RecordPath, HttpStatusCode.Unauthorized, UnauthorizedBody);
        await AddAsync();

        _clock.Advance(TimeSpan.FromMinutes(30) - TimeSpan.FromSeconds(1));
        AssertUnreachable(await AddAsync(), "authentication failed");
        AssertSent(PersonCreate);

        _clock.Advance(TimeSpan.FromSeconds(1));
        AssertUnreachable(await AddAsync(), "authentication failed");
        AssertSent(PersonCreate, PersonCreate);
    }

    [Fact]
    public async Task A_locked_account_is_held_until_exactly_its_unlock_time()
    {
        _reader.Answer(RecordPath, HttpStatusCode.Unauthorized, LockedBody);

        AssertUnreachable(await AddAsync(), "account locked, unlocks in 120 s");

        _clock.Advance(TimeSpan.FromSeconds(119));
        AssertUnreachable(await AddAsync(), "account locked, unlocks in 120 s");
        AssertSent(PersonCreate);

        _clock.Advance(TimeSpan.FromSeconds(1));
        _reader.Answer(RecordPath, HttpStatusCode.OK, ScriptedReader.OkBody);
        AssertDelivered(await AddAsync());
        AssertSent(PersonCreate, PersonCreate, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task Changed_credentials_are_tried_at_once()
    {
        _reader.Answer(RecordPath, HttpStatusCode.Unauthorized, UnauthorizedBody);
        await AddAsync();
        _reader.Answer(RecordPath, HttpStatusCode.OK, ScriptedReader.OkBody);

        var outcome = await _client.AddAsync(
            FrontGate with { Password = "Front-Pa56" },
            Spectator,
            CancellationToken.None
        );

        AssertDelivered(outcome);
        AssertSent(PersonCreate, PersonCreate, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task Refused_credentials_at_the_face_step_are_held_and_nothing_is_compensated()
    {
        _reader.Answer(FacePath, HttpStatusCode.Forbidden, "");

        AssertUnreachable(await AddAsync(), "authentication failed");
        AssertUnreachable(await AddAsync(), "authentication failed");

        AssertSent(PersonCreate, FaceDelete, FaceCreate);
    }

    [Fact]
    public async Task Refused_credentials_during_a_remove_are_held()
    {
        _reader.Answer(PersonDeletePath, HttpStatusCode.Unauthorized, UnauthorizedBody);

        AssertUnreachable(await RemoveAsync(), "authentication failed");
        AssertUnreachable(await RemoveAsync(), "authentication failed");

        AssertSent(FaceDelete, PersonDelete);
    }
}
