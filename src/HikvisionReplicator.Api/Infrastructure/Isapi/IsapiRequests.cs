namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>
/// The only place that knows ISAPI paths and body shapes (design § IsapiRequests). When the bench
/// UAT corrects A-1 or A-7, this is the file that changes.
/// </summary>
public static class IsapiRequests
{
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
}
