using System.Net;
using System.Text;

namespace HikvisionReplicator.Tests.Infrastructure.Isapi;

/// <summary>
/// A reader with no socket: it records every request it receives (method, path with query, body)
/// and answers each ISAPI path as scripted — <c>statusCode 1</c> unless told otherwise.
/// </summary>
internal sealed class ScriptedReader : HttpMessageHandler
{
    public const string OkBody = """{"statusCode":1,"statusString":"OK","subStatusCode":"ok"}""";

    private readonly Dictionary<string, Func<CancellationToken, HttpResponseMessage>> _answers = [];

    /// <summary>Every request received, in order, as <c>"METHOD /path?query"</c>.</summary>
    public List<string> Sent { get; } = [];

    /// <summary>The body of each request in <see cref="Sent"/>, same index.</summary>
    public List<string> Bodies { get; } = [];

    /// <summary>Runs before each request is answered — a test's hook for advancing a clock.</summary>
    public Action? OnEachRequest { get; set; }

    public void Answer(string path, HttpStatusCode status, string body, string? challenge = null) =>
        _answers[path] = _ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (challenge is not null)
                response.Headers.WwwAuthenticate.ParseAdd(challenge);
            return response;
        };

    /// <summary>The path's request never gets an answer: the transport fails with <paramref name="error"/>.</summary>
    public void Fail(string path, HttpRequestError error) =>
        _answers[path] = _ => throw new HttpRequestException(error, "scripted transport failure");

    /// <summary>The path's request is abandoned without the caller's token being cancelled.</summary>
    public void Abandon(string path) =>
        _answers[path] = _ => throw new TaskCanceledException("scripted abandonment");

    /// <summary>The caller's token is cancelled while the path's request is in flight.</summary>
    public void CancelDuring(string path, CancellationTokenSource caller) =>
        _answers[path] = token =>
        {
            caller.Cancel();
            token.ThrowIfCancellationRequested();
            throw new InvalidOperationException("the caller's token did not reach the handler");
        };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken
    )
    {
        var uri = request.RequestUri!;
        Sent.Add($"{request.Method} {uri.PathAndQuery}");
        Bodies.Add(
            request.Content is null
                ? ""
                : await request.Content.ReadAsStringAsync(cancellationToken)
        );
        OnEachRequest?.Invoke();

        return _answers.TryGetValue(uri.AbsolutePath, out var answer)
            ? answer(cancellationToken)
            : new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(OkBody, Encoding.UTF8, "application/json"),
            };
    }
}
