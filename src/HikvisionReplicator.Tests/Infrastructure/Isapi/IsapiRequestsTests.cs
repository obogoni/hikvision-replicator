using System.Text.Json;
using HikvisionReplicator.Api.Infrastructure.Isapi;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// The ISAPI shapes each step sends (A-1, A-7, A-8, ISA-01). Paths, queries and fields are pinned
/// as literals: they are what the reader parses, and the bench UAT corrects them here. The key
/// for <c>TICKET-0001</c> is <c>b43a95a2c19a97b6289861e47299284f</c>, computed with
/// <c>sha256sum</c>.
/// </summary>
public class IsapiRequestsTests
{
    private const string Key = "b43a95a2c19a97b6289861e47299284f";

    private static readonly byte[] Face = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0xFF, 0xD9];

    private static readonly DeviceEnrolment Spectator = new("TICKET-0001", "Ana Souza", "654321", Face);

    private static readonly IsapiOptions Defaults = new();

    private static async Task<JsonElement> JsonBodyAsync(HttpContent content)
    {
        using var document = JsonDocument.Parse(await content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }

    private static async Task AssertPersonBodyAsync(HttpRequestMessage request, string name)
    {
        var user = (await JsonBodyAsync(request.Content!)).GetProperty("UserInfo");

        Assert.Equal(Key, user.GetProperty("employeeNo").GetString());
        Assert.Equal(name, user.GetProperty("name").GetString());
        Assert.Equal("normal", user.GetProperty("userType").GetString());
        Assert.Equal("654321", user.GetProperty("password").GetString());
        var valid = user.GetProperty("Valid");
        Assert.True(valid.GetProperty("enable").GetBoolean());
        Assert.Equal("2000-01-01T00:00:00", valid.GetProperty("beginTime").GetString());
        Assert.Equal("2037-12-31T23:59:59", valid.GetProperty("endTime").GetString());
        Assert.Equal("1", user.GetProperty("doorRight").GetString());
        var plan = Assert.Single(user.GetProperty("RightPlan").EnumerateArray());
        Assert.Equal(1, plan.GetProperty("doorNo").GetInt32());
        Assert.Equal("1", plan.GetProperty("planTemplateNo").GetString());
    }

    // ─── The person ───

    [Fact]
    public async Task A_new_person_is_recorded_with_key_name_pin_and_a_permanent_right()
    {
        using var request = IsapiRequests.PersonCreate(Spectator, Defaults);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/ISAPI/AccessControl/UserInfo/Record?format=json", request.RequestUri!.OriginalString);
        await AssertPersonBodyAsync(request, "Ana Souza");
    }

    [Fact]
    public async Task An_existing_person_is_modified_with_the_same_body()
    {
        using var request = IsapiRequests.PersonModify(Spectator, Defaults);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/ISAPI/AccessControl/UserInfo/Modify?format=json", request.RequestUri!.OriginalString);
        await AssertPersonBodyAsync(request, "Ana Souza");
    }

    [Fact]
    public async Task A_name_longer_than_the_reader_holds_is_sent_truncated()
    {
        var options = new IsapiOptions { NameMaxLength = 9 };
        var spectator = Spectator with { Name = "Ana Souza Lima" };

        using var create = IsapiRequests.PersonCreate(spectator, options);
        using var modify = IsapiRequests.PersonModify(spectator, options);

        await AssertPersonBodyAsync(create, "Ana Souza");
        await AssertPersonBodyAsync(modify, "Ana Souza");
    }

    [Fact]
    public async Task The_right_granted_follows_the_configured_door_plan_and_window()
    {
        var options = new IsapiOptions
        {
            DoorNo = 2,
            PlanTemplateNo = "7",
            ValidFrom = new DateTime(2026, 1, 1, 6, 0, 0),
            ValidTo = new DateTime(2030, 6, 30, 22, 30, 15),
        };

        using var request = IsapiRequests.PersonCreate(Spectator, options);

        var user = (await JsonBodyAsync(request.Content!)).GetProperty("UserInfo");
        Assert.Equal("2026-01-01T06:00:00", user.GetProperty("Valid").GetProperty("beginTime").GetString());
        Assert.Equal("2030-06-30T22:30:15", user.GetProperty("Valid").GetProperty("endTime").GetString());
        Assert.Equal("2", user.GetProperty("doorRight").GetString());
        var plan = Assert.Single(user.GetProperty("RightPlan").EnumerateArray());
        Assert.Equal(2, plan.GetProperty("doorNo").GetInt32());
        Assert.Equal("7", plan.GetProperty("planTemplateNo").GetString());
    }

    [Fact]
    public async Task A_person_is_deleted_by_key()
    {
        using var request = IsapiRequests.PersonDelete("TICKET-0001");

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal("/ISAPI/AccessControl/UserInfo/Delete?format=json", request.RequestUri!.OriginalString);
        var entry = Assert.Single(
            (await JsonBodyAsync(request.Content!))
                .GetProperty("UserInfoDelCond")
                .GetProperty("EmployeeNoList")
                .EnumerateArray()
        );
        Assert.Equal(Key, entry.GetProperty("employeeNo").GetString());
    }

    // ─── The face ───

    [Fact]
    public async Task A_face_is_deleted_by_key_from_the_configured_library()
    {
        using var request = IsapiRequests.FaceDelete("TICKET-0001", Defaults);

        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal(
            "/ISAPI/Intelligent/FDLib/FDSearch/Delete?format=json&FDID=1&faceLibType=blackFD",
            request.RequestUri!.OriginalString
        );
        var fpid = Assert.Single((await JsonBodyAsync(request.Content!)).GetProperty("FPID").EnumerateArray());
        Assert.Equal(Key, fpid.GetProperty("value").GetString());
    }

    [Fact]
    public async Task A_face_is_recorded_as_its_record_then_its_jpeg_in_that_order()
    {
        using var request = IsapiRequests.FaceCreate(Spectator, Defaults);

        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("/ISAPI/Intelligent/FDLib/FaceDataRecord?format=json", request.RequestUri!.OriginalString);
        var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
        Assert.Equal("multipart/form-data", multipart.Headers.ContentType!.MediaType);
        var parts = multipart.ToList();
        Assert.Equal(2, parts.Count);

        var record = parts[0];
        Assert.Equal("FaceDataRecord", record.Headers.ContentDisposition!.Name);
        var fields = await JsonBodyAsync(record);
        Assert.Equal("blackFD", fields.GetProperty("faceLibType").GetString());
        Assert.Equal("1", fields.GetProperty("FDID").GetString());
        Assert.Equal(Key, fields.GetProperty("FPID").GetString());

        var image = parts[1];
        Assert.Equal("FaceImage", image.Headers.ContentDisposition!.Name);
        Assert.Equal("image/jpeg", image.Headers.ContentType!.MediaType);
        Assert.Equal(Face, await image.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task The_face_steps_follow_the_configured_library()
    {
        var options = new IsapiOptions { FaceLibraryId = "2", FaceLibraryType = "staticFD" };

        using var delete = IsapiRequests.FaceDelete("TICKET-0001", options);
        using var create = IsapiRequests.FaceCreate(Spectator, options);

        Assert.Equal(
            "/ISAPI/Intelligent/FDLib/FDSearch/Delete?format=json&FDID=2&faceLibType=staticFD",
            delete.RequestUri!.OriginalString
        );
        var fields = await JsonBodyAsync(((MultipartFormDataContent)create.Content!).First());
        Assert.Equal("staticFD", fields.GetProperty("faceLibType").GetString());
        Assert.Equal("2", fields.GetProperty("FDID").GetString());
    }
}
