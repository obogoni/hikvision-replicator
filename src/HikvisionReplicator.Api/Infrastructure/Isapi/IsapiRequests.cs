using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>
/// The only place that knows ISAPI paths and body shapes (design § IsapiRequests). When the bench
/// UAT corrects A-1 or A-7, this is the file that changes. Every request is relative, to be sent
/// on a client whose base address is the reader.
/// </summary>
public static class IsapiRequests
{
    private const string ReaderTimeFormat = "yyyy-MM-dd'T'HH:mm:ss";

    /// <summary>The path a step calls, without its query — what a 404 or 405 reason names (ISA-16).</summary>
    public static string PathOf(IsapiStep step) =>
        step switch
        {
            IsapiStep.PersonCreate => "/ISAPI/AccessControl/UserInfo/Record",
            IsapiStep.PersonModify => "/ISAPI/AccessControl/UserInfo/Modify",
            IsapiStep.PersonDelete => "/ISAPI/AccessControl/UserInfo/Delete",
            IsapiStep.FaceDelete => "/ISAPI/Intelligent/FDLib/FDSearch/Delete",
            IsapiStep.FaceCreate => "/ISAPI/Intelligent/FDLib/FaceDataRecord",
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
        };

    /// <summary>Writes a new person: key, fitted name, PIN and the one permissive right (A-7, A-8).</summary>
    public static HttpRequestMessage PersonCreate(DeviceEnrolment enrolment, IsapiOptions options) =>
        Json(HttpMethod.Post, IsapiStep.PersonCreate, PersonBody(enrolment, options));

    /// <summary>Converges an existing person to the enrolment (ISA-02, ISA-07).</summary>
    public static HttpRequestMessage PersonModify(DeviceEnrolment enrolment, IsapiOptions options) =>
        Json(HttpMethod.Put, IsapiStep.PersonModify, PersonBody(enrolment, options));

    public static HttpRequestMessage PersonDelete(string externalRef) =>
        Json(
            HttpMethod.Put,
            IsapiStep.PersonDelete,
            new
            {
                UserInfoDelCond = new
                {
                    EmployeeNoList = new[] { new { employeeNo = IsapiIdentity.EmployeeNo(externalRef) } },
                },
            }
        );

    public static HttpRequestMessage FaceDelete(string externalRef, IsapiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var request = Json(
            HttpMethod.Put,
            IsapiStep.FaceDelete,
            new { FPID = new[] { new { value = IsapiIdentity.EmployeeNo(externalRef) } } }
        );
        request.RequestUri = Relative(
            IsapiStep.FaceDelete,
            $"&FDID={Uri.EscapeDataString(options.FaceLibraryId)}"
                + $"&faceLibType={Uri.EscapeDataString(options.FaceLibraryType)}"
        );
        return request;
    }

    /// <summary>
    /// The face, as two multipart parts in a strict order: the <c>FaceDataRecord</c> JSON, then
    /// the <c>FaceImage</c> JPEG (A-1).
    /// </summary>
    public static HttpRequestMessage FaceCreate(DeviceEnrolment enrolment, IsapiOptions options)
    {
        ArgumentNullException.ThrowIfNull(enrolment);
        ArgumentNullException.ThrowIfNull(options);

        var record = JsonContentOf(
            new
            {
                faceLibType = options.FaceLibraryType,
                FDID = options.FaceLibraryId,
                FPID = IsapiIdentity.EmployeeNo(enrolment.ExternalRef),
            }
        );
        var image = new ReadOnlyMemoryContent(enrolment.FacePicture);
        image.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        var content = new MultipartFormDataContent
        {
            { record, "FaceDataRecord" },
            { image, "FaceImage", "face.jpg" },
        };

        return new HttpRequestMessage(HttpMethod.Post, Relative(IsapiStep.FaceCreate))
        {
            Content = content,
        };
    }

    private static object PersonBody(DeviceEnrolment enrolment, IsapiOptions options)
    {
        ArgumentNullException.ThrowIfNull(enrolment);
        ArgumentNullException.ThrowIfNull(options);

        return new
        {
            UserInfo = new
            {
                employeeNo = IsapiIdentity.EmployeeNo(enrolment.ExternalRef),
                name = IsapiIdentity.TruncateName(enrolment.Name, options.NameMaxLength),
                userType = "normal",
                Valid = new
                {
                    enable = true,
                    beginTime = options.ValidFrom.ToString(ReaderTimeFormat, CultureInfo.InvariantCulture),
                    endTime = options.ValidTo.ToString(ReaderTimeFormat, CultureInfo.InvariantCulture),
                },
                doorRight = options.DoorNo.ToString(CultureInfo.InvariantCulture),
                RightPlan = new[]
                {
                    new { doorNo = options.DoorNo, planTemplateNo = options.PlanTemplateNo },
                },
                password = enrolment.AccessCode,
            },
        };
    }

    private static HttpRequestMessage Json(HttpMethod method, IsapiStep step, object body) =>
        new(method, Relative(step)) { Content = JsonContentOf(body) };

    private static StringContent JsonContentOf(object body) =>
        new(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

    private static Uri Relative(IsapiStep step, string query = "") =>
        new($"{PathOf(step)}?format=json{query}", UriKind.Relative);
}
