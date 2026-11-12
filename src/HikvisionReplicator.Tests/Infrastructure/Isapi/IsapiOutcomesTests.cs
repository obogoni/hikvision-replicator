using System.Net;
using HikvisionReplicator.Api.Infrastructure.Isapi;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// The single classification rule (A-4, ISA-12…ISA-18, ISA-25): one test per row of the design's
/// table. Reasons are pinned as literal text — the worker dead-letters or pauses on what these
/// say, and an operator reads them.
/// </summary>
public class IsapiOutcomesTests
{
    private static readonly IsapiOptions Defaults = new();

    private static StepResult Classify(
        HttpStatusCode status,
        string body,
        IsapiStep step = IsapiStep.FaceCreate,
        IsapiOptions? options = null
    ) => IsapiOutcomes.Classify(step, status, body, options ?? Defaults);

    // ─── ISA-12: success ───

    [Fact]
    public void A_2xx_answer_with_status_code_one_is_ok()
    {
        var result = Classify(
            HttpStatusCode.OK,
            """{"statusCode":1,"statusString":"OK","subStatusCode":"ok"}"""
        );

        Assert.IsType<StepResult.Ok>(result);
    }

    // ─── ISA-02 / ISA-11: answers the sequence continues past ───

    [Fact]
    public void A_configured_already_exists_code_means_the_person_is_already_there()
    {
        var result = Classify(
            HttpStatusCode.BadRequest,
            """{"statusCode":6,"statusString":"Invalid Content","subStatusCode":"deviceUserAlreadyExist","errorCode":1610637344,"errorMsg":"exists"}""",
            IsapiStep.PersonCreate
        );

        Assert.IsType<StepResult.AlreadyExists>(result);
    }

    [Fact]
    public void A_configured_absent_code_means_there_was_nothing_to_delete()
    {
        var options = new IsapiOptions { AbsentSubStatusCodes = ["employeeNoNotExist"] };

        var result = Classify(
            HttpStatusCode.BadRequest,
            """{"statusCode":6,"subStatusCode":"employeeNoNotExist"}""",
            IsapiStep.PersonDelete,
            options
        );

        Assert.IsType<StepResult.Absent>(result);
    }

    [Fact]
    public void An_absent_code_that_is_not_configured_is_a_refusal()
    {
        var result = Classify(
            HttpStatusCode.BadRequest,
            """{"statusCode":6,"subStatusCode":"employeeNoNotExist"}""",
            IsapiStep.PersonDelete
        );

        Assert.Equal(
            new StepResult.Refused("person-delete: employeeNoNotExist", "employeeNoNotExist"),
            result
        );
    }

    // ─── ISA-15: credentials refused ───

    [Fact]
    public void A_401_is_an_authentication_failure()
    {
        var result = Classify(HttpStatusCode.Unauthorized, "");

        Assert.Equal(new StepResult.AuthFailed("authentication failed", null), result);
    }

    [Fact]
    public void A_403_is_an_authentication_failure()
    {
        var result = Classify(
            HttpStatusCode.Forbidden,
            """{"statusCode":4,"subStatusCode":"notSupport"}"""
        );

        Assert.Equal(new StepResult.AuthFailed("authentication failed", null), result);
    }

    [Fact]
    public void A_401_reporting_a_locked_account_in_json_names_the_lock_and_its_unlock_time()
    {
        var result = Classify(
            HttpStatusCode.Unauthorized,
            """{"userCheck":{"statusValue":401,"statusString":"Unauthorized","isActivated":true,"lockStatus":"lock","unlockTime":1800,"retryLoginTime":0}}"""
        );

        Assert.Equal(
            new StepResult.AuthFailed("account locked, unlocks in 1800 s", TimeSpan.FromSeconds(1800)),
            result
        );
    }

    [Fact]
    public void A_401_reporting_a_locked_account_in_xml_names_the_lock_and_its_unlock_time()
    {
        var result = Classify(
            HttpStatusCode.Unauthorized,
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <userCheck version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
              <statusValue>401</statusValue>
              <statusString>Unauthorized</statusString>
              <lockStatus>lock</lockStatus>
              <unlockTime>1795</unlockTime>
              <retryLoginTime>0</retryLoginTime>
            </userCheck>
            """
        );

        Assert.Equal(
            new StepResult.AuthFailed("account locked, unlocks in 1795 s", TimeSpan.FromSeconds(1795)),
            result
        );
    }

    [Fact]
    public void A_locked_account_without_an_unlock_time_is_reported_as_locked()
    {
        var result = Classify(
            HttpStatusCode.Unauthorized,
            """{"userCheck":{"statusValue":401,"lockStatus":"lock"}}"""
        );

        Assert.Equal(new StepResult.AuthFailed("account locked", null), result);
    }

    [Fact]
    public void A_401_whose_account_is_unlocked_is_a_plain_authentication_failure()
    {
        var result = Classify(
            HttpStatusCode.Unauthorized,
            """<userCheck><statusValue>401</statusValue><lockStatus>unlock</lockStatus><retryLoginTime>4</retryLoginTime></userCheck>"""
        );

        Assert.Equal(new StepResult.AuthFailed("authentication failed", null), result);
    }

    // ─── ISA-16: a path the firmware does not have ───

    [Theory]
    [InlineData(IsapiStep.PersonCreate, HttpStatusCode.NotFound, "/ISAPI/AccessControl/UserInfo/Record not supported")]
    [InlineData(IsapiStep.PersonModify, HttpStatusCode.MethodNotAllowed, "/ISAPI/AccessControl/UserInfo/Modify not supported")]
    [InlineData(IsapiStep.PersonDelete, HttpStatusCode.NotFound, "/ISAPI/AccessControl/UserInfo/Delete not supported")]
    [InlineData(IsapiStep.FaceDelete, HttpStatusCode.NotFound, "/ISAPI/Intelligent/FDLib/FDSearch/Delete not supported")]
    [InlineData(IsapiStep.FaceCreate, HttpStatusCode.MethodNotAllowed, "/ISAPI/Intelligent/FDLib/FaceDataRecord not supported")]
    public void A_404_or_405_means_the_reader_does_not_support_the_path(
        IsapiStep step,
        HttpStatusCode status,
        string reason
    )
    {
        var result = Classify(status, """{"statusCode":4,"subStatusCode":"notSupport"}""", step);

        Assert.Equal(new StepResult.ReaderDown(reason), result);
    }

    // ─── ISA-17: the reader failing ───

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError, "HTTP 500")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "HTTP 503")]
    public void A_5xx_means_the_reader_is_down_with_the_status_in_the_reason(
        HttpStatusCode status,
        string reason
    )
    {
        var result = Classify(
            status,
            """{"statusCode":2,"subStatusCode":"deviceBusy","errorMsg":"busy"}"""
        );

        Assert.Equal(new StepResult.ReaderDown(reason), result);
    }

    // ─── ISA-13: the reader refusing this record ───

    [Fact]
    public void A_refusal_names_the_step_sub_status_error_code_and_message()
    {
        var result = Classify(
            HttpStatusCode.BadRequest,
            """{"statusCode":6,"statusString":"Invalid Content","subStatusCode":"faceQualityTooLow","errorCode":1610637355,"errorMsg":"face quality too low"}"""
        );

        Assert.Equal(
            new StepResult.Refused(
                "face: faceQualityTooLow (1610637355) face quality too low",
                "faceQualityTooLow"
            ),
            result
        );
    }

    [Fact]
    public void A_refusal_omits_the_parts_the_reader_left_out()
    {
        var result = Classify(
            HttpStatusCode.BadRequest,
            """{"statusCode":6,"subStatusCode":"badJsonContent"}""",
            IsapiStep.PersonCreate
        );

        Assert.Equal(new StepResult.Refused("person: badJsonContent", "badJsonContent"), result);
    }

    [Fact]
    public void A_refusal_with_no_detail_names_the_step_alone()
    {
        var result = Classify(HttpStatusCode.BadRequest, """{"statusCode":6}""");

        Assert.Equal(new StepResult.Refused("face", null), result);
    }

    [Fact]
    public void A_refusal_on_a_2xx_answer_is_still_a_refusal()
    {
        var result = Classify(
            HttpStatusCode.OK,
            """{"statusCode":4,"subStatusCode":"invalidOperation","errorCode":1073741825}"""
        );

        Assert.Equal(
            new StepResult.Refused("face: invalidOperation (1073741825)", "invalidOperation"),
            result
        );
    }

    [Fact]
    public void A_refusal_in_xml_is_read_like_one_in_json()
    {
        var result = Classify(
            HttpStatusCode.BadRequest,
            """
            <ResponseStatus version="2.0" xmlns="http://www.hikvision.com/ver20/XMLSchema">
              <statusCode>6</statusCode>
              <statusString>Invalid Content</statusString>
              <subStatusCode>badXmlContent</subStatusCode>
            </ResponseStatus>
            """
        );

        Assert.Equal(new StepResult.Refused("face: badXmlContent", "badXmlContent"), result);
    }

    [Fact]
    public void A_refusal_message_is_capped_at_200_characters()
    {
        var message = new string('m', 200) + "TAIL";

        var result = Classify(
            HttpStatusCode.BadRequest,
            $$"""{"statusCode":6,"subStatusCode":"x","errorMsg":"{{message}}"}"""
        );

        Assert.Equal(new StepResult.Refused($"face: x {new string('m', 200)}", "x"), result);
    }

    [Fact]
    public void A_refusal_reason_carries_none_of_the_bodys_other_fields()
    {
        var result = Classify(
            HttpStatusCode.BadRequest,
            """
            {"statusCode":6,"statusString":"Invalid Content","subStatusCode":"badFace",
             "password":"hunter2-Secret","UserInfo":{"name":"Ana Souza","password":"654321"},
             "debug":"Authorization: Digest username=\"admin\""}
            """
        );

        var refused = Assert.IsType<StepResult.Refused>(result);
        Assert.Equal("face: badFace", refused.Reason);
        Assert.DoesNotContain("hunter2", refused.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("654321", refused.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Digest", refused.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid Content", refused.Reason, StringComparison.Ordinal);
    }

    // ─── ISA-18: an answer that is not a ResponseStatus ───

    [Theory]
    [InlineData(HttpStatusCode.OK, "<html><body>Welcome</body></html>", "unrecognised response, HTTP 200")]
    [InlineData(HttpStatusCode.BadRequest, "", "unrecognised response, HTTP 400")]
    [InlineData(HttpStatusCode.OK, """{"statusString":"OK"}""", "unrecognised response, HTTP 200")]
    [InlineData(HttpStatusCode.Conflict, "{not json", "unrecognised response, HTTP 409")]
    public void An_answer_that_is_not_a_response_status_is_unrecognised(
        HttpStatusCode status,
        string body,
        string reason
    )
    {
        Assert.Equal(new StepResult.ReaderDown(reason), Classify(status, body));
    }

    // ─── ISA-14: never reached the reader ───

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, "NameResolutionError")]
    [InlineData(HttpRequestError.ConnectionError, "ConnectionError")]
    [InlineData(HttpRequestError.SecureConnectionError, "SecureConnectionError")]
    [InlineData(HttpRequestError.ResponseEnded, "ResponseEnded")]
    [InlineData(HttpRequestError.Unknown, "Unknown")]
    public void A_transport_failure_means_the_reader_is_down_naming_the_failure_class(
        HttpRequestError error,
        string reason
    )
    {
        var exception = new HttpRequestException(error, "socket detail 10.0.0.1:443 secret-ish");

        var result = IsapiOutcomes.Classify(IsapiStep.FaceCreate, exception);

        Assert.Equal(new StepResult.ReaderDown(reason), result);
    }
}
