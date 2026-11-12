using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace HikvisionReplicator.Api.Infrastructure.Isapi;

/// <summary>One authenticated exchange with a reader. A port call is a short sequence of these.</summary>
public enum IsapiStep
{
    PersonCreate,
    PersonModify,
    PersonDelete,
    FaceDelete,
    FaceCreate,
}

/// <summary>
/// What one step's answer means (design § IsapiOutcomes). The client turns the first result that
/// is not <see cref="Ok"/> into the call's outcome, except where the sequence continues
/// (<see cref="AlreadyExists"/> on the person, <see cref="Absent"/> on a delete).
/// </summary>
public abstract record StepResult
{
    private StepResult() { }

    /// <summary>HTTP 2xx with <c>statusCode</c> 1 (ISA-12).</summary>
    public sealed record Ok : StepResult;

    /// <summary>The reader already holds this person (ISA-02).</summary>
    public sealed record AlreadyExists : StepResult;

    /// <summary>There was nothing to delete (ISA-11).</summary>
    public sealed record Absent : StepResult;

    /// <summary>The reader refused this record (ISA-13) — <c>Rejected</c> at the port.</summary>
    public sealed record Refused(string Reason, string? SubStatusCode) : StepResult;

    /// <summary>The reader could not judge any record (ISA-14, ISA-16…ISA-18) — <c>Unreachable</c>.</summary>
    public sealed record ReaderDown(string Reason) : StepResult;

    /// <summary>
    /// The reader refused the credentials (ISA-15) — <c>Unreachable</c>, and the key is held
    /// (ISA-37). <paramref name="UnlockAfter"/> is how long the reader said its lock lasts, when it
    /// said.
    /// </summary>
    public sealed record AuthFailed(string Reason, TimeSpan? UnlockAfter) : StepResult;
}

/// <summary>
/// The single classification rule (A-4): a failure that would hit every spectator on the reader
/// is <see cref="StepResult.ReaderDown"/> or <see cref="StepResult.AuthFailed"/>; a failure of
/// this spectator is <see cref="StepResult.Refused"/>. A reason is assembled only from the step,
/// the HTTP status and named <c>ResponseStatus</c> fields — never from a raw body (ISA-25).
/// </summary>
public static class IsapiOutcomes
{
    /// <summary>The longest <c>errorMsg</c> a reason carries.</summary>
    public const int ErrorMsgMaxLength = 200;

    /// <summary>The step as it is named in reasons, spans and logs.</summary>
    public static string NameOf(IsapiStep step) =>
        step switch
        {
            IsapiStep.PersonCreate => "person",
            IsapiStep.PersonModify => "person-modify",
            IsapiStep.PersonDelete => "person-delete",
            IsapiStep.FaceDelete => "face-delete",
            IsapiStep.FaceCreate => "face",
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
        };

    public static StepResult Classify(
        IsapiStep step,
        HttpStatusCode status,
        string body,
        IsapiOptions options
    )
    {
        ArgumentNullException.ThrowIfNull(options);

        var code = (int)status;
        var response = ResponseStatus.TryParse(body);

        if (response is not null)
        {
            if (code is >= 200 and < 300 && response.StatusCode == 1)
                return new StepResult.Ok();

            if (response.SubStatusCode is { } sub)
            {
                if (options.AlreadyExistsSubStatusCodes.Contains(sub))
                    return new StepResult.AlreadyExists();

                if (options.AbsentSubStatusCodes.Contains(sub))
                    return new StepResult.Absent();
            }
        }

        if (status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return AuthFailure(body);

        if (status is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
            return new StepResult.ReaderDown($"{IsapiRequests.PathOf(step)} not supported");

        if (code is >= 500 and < 600)
            return new StepResult.ReaderDown($"HTTP {code}");

        if (response is not null && response.StatusCode != 1)
            return new StepResult.Refused(RefusalReason(step, response), response.SubStatusCode);

        return new StepResult.ReaderDown($"unrecognised response, HTTP {code}");
    }

    /// <summary>A transport failure (ISA-14): the reason names its class and nothing else.</summary>
    public static StepResult Classify(IsapiStep step, HttpRequestException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new StepResult.ReaderDown(exception.HttpRequestError.ToString());
    }

    private static string RefusalReason(IsapiStep step, ResponseStatus response)
    {
        var message = response.ErrorMsg is { Length: > ErrorMsgMaxLength } longMessage
            ? longMessage[..ErrorMsgMaxLength]
            : response.ErrorMsg;

        var parts = new[]
        {
            response.SubStatusCode,
            response.ErrorCode is null ? null : $"({response.ErrorCode})",
            message,
        }.Where(part => !string.IsNullOrWhiteSpace(part));

        var detail = string.Join(' ', parts);
        return detail.Length == 0 ? NameOf(step) : $"{NameOf(step)}: {detail}";
    }

    /// <summary>
    /// ISA-15 / A-14. The lock report is read tolerantly — JSON or XML, any nesting — and only
    /// its lock status and unlock time are taken from it.
    /// </summary>
    private static StepResult.AuthFailed AuthFailure(string body)
    {
        var fields = Fields.Read(body);
        var locked = string.Equals(
            fields.GetValueOrDefault("lockStatus"),
            "lock",
            StringComparison.OrdinalIgnoreCase
        );

        if (!locked)
            return new StepResult.AuthFailed("authentication failed", null);

        // SPEC_DEVIATION: the design's AuthFailed carries an absolute unlockAt; this carries the
        // remaining lock time instead.
        // Reason: the reader reports unlockTime as seconds remaining, and Classify is pure — it
        // has no clock to turn that into an instant. The client adds it to its TimeProvider.
        if (
            int.TryParse(
                fields.GetValueOrDefault("unlockTime"),
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var seconds
            )
        )
        {
            return new StepResult.AuthFailed(
                $"account locked, unlocks in {seconds} s",
                TimeSpan.FromSeconds(seconds)
            );
        }

        return new StepResult.AuthFailed("account locked", null);
    }

    private sealed record ResponseStatus(
        int StatusCode,
        string? SubStatusCode,
        string? ErrorCode,
        string? ErrorMsg
    )
    {
        /// <summary>A <c>ResponseStatus</c> must at least carry an integer <c>statusCode</c>.</summary>
        public static ResponseStatus? TryParse(string body)
        {
            var fields = Fields.Read(body);
            if (
                !int.TryParse(
                    fields.GetValueOrDefault("statusCode"),
                    NumberStyles.AllowLeadingSign,
                    CultureInfo.InvariantCulture,
                    out var statusCode
                )
            )
                return null;

            return new ResponseStatus(
                statusCode,
                Blank(fields.GetValueOrDefault("subStatusCode")),
                Blank(fields.GetValueOrDefault("errorCode")),
                Blank(fields.GetValueOrDefault("errorMsg"))
            );
        }

        private static string? Blank(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// The scalar fields of a JSON or XML body, first occurrence of each name at any depth. An
    /// empty map when the body is neither.
    /// </summary>
    private static class Fields
    {
        public static Dictionary<string, string> Read(string body)
        {
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);
            var trimmed = body.AsSpan().TrimStart();

            if (trimmed.StartsWith("{"))
                ReadJson(body, fields);
            else if (trimmed.StartsWith("<"))
                ReadXml(body, fields);

            return fields;
        }

        private static void ReadJson(string body, Dictionary<string, string> fields)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                Walk(document.RootElement, fields);
            }
            catch (JsonException) { }
        }

        private static void Walk(JsonElement element, Dictionary<string, string> fields)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (var property in element.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    case JsonValueKind.Object:
                        Walk(property.Value, fields);
                        break;
                    case JsonValueKind.String:
                        fields.TryAdd(property.Name, property.Value.GetString()!);
                        break;
                    case JsonValueKind.Number:
                        fields.TryAdd(property.Name, property.Value.GetRawText());
                        break;
                }
            }
        }

        private static void ReadXml(string body, Dictionary<string, string> fields)
        {
            try
            {
                var document = XDocument.Parse(body);
                foreach (var element in document.Descendants().Where(e => !e.HasElements))
                    fields.TryAdd(element.Name.LocalName, element.Value.Trim());
            }
            catch (XmlException) { }
        }
    }
}
