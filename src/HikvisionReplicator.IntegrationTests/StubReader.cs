using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HikvisionReplicator.Api.Infrastructure.Isapi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// A Hikvision reader on loopback, for the tests that drive the ISAPI adapter end to end:
/// Kestrel on <c>127.0.0.1</c> at a port the operating system picks, over HTTPS with a
/// self-signed certificate made in memory — the shape a real reader presents (A-5).
/// <para>
/// Authentication is a <b>real</b> RFC 7616 digest exchange (MD5, <c>qop="auth"</c>): a request
/// without credentials is challenged, and a response computed from any other password is
/// refused and counted as a failed login. That count is what proves the adapter cannot lock a
/// reader's account (ISA-37).
/// </para>
/// <para>
/// Persons and faces live in memory, keyed by <c>employeeNo</c>. A delete of something absent
/// answers <see cref="NothingThere"/>, which a test configures as the adapter's absent code. Like
/// a real reader, deleting a person leaves its face behind (ISA-10).
/// </para>
/// </summary>
public sealed partial class StubReader : IAsyncDisposable
{
    public const string Username = "admin";
    public const string Realm = "stub-reader";

    /// <summary>What this reader answers when there is nothing to delete or modify.</summary>
    public const string NothingThere = "notExist";

    /// <summary>What it answers when the person being created is already there (ISA-02).</summary>
    public const string PersonAlreadyThere = "deviceUserAlreadyExist";

    /// <summary>What it answers when a face is written over one it already holds.</summary>
    public const string FaceAlreadyThere = "deviceUserAlreadyExistFace";

    private const string FaceLibraryId = "1";
    private const string FaceLibraryType = "blackFD";

    private readonly WebApplication _app;
    private readonly string _password;
    private readonly ConcurrentDictionary<string, byte> _nonces = new();
    private readonly ConcurrentDictionary<string, StubPerson> _persons = new();
    private readonly ConcurrentDictionary<string, byte[]> _faces = new();
    private readonly ConcurrentDictionary<IsapiStep, Func<HttpContext, Task>> _scripts = new();
    private int _failedLogins;
    private volatile bool _offline;

    private StubReader(WebApplication app, string password, X509Certificate2 certificate)
    {
        _app = app;
        _password = password;
        Certificate = certificate;
    }

    /// <summary>The self-signed certificate it presents, for a test client to trust exactly.</summary>
    public X509Certificate2 Certificate { get; }

    /// <summary>The port it listens on — what a device is registered with.</summary>
    public int Port { get; private set; }

    public Uri BaseAddress => new($"https://127.0.0.1:{Port}/");

    /// <summary>Authenticated requests whose digest did not match the reader's password.</summary>
    public int FailedLogins => Volatile.Read(ref _failedLogins);

    /// <summary>The persons it holds, by <c>employeeNo</c>.</summary>
    public IReadOnlyDictionary<string, StubPerson> Persons => new Dictionary<string, StubPerson>(_persons);

    /// <summary>The face bytes it holds, by <c>FPID</c>.</summary>
    public IReadOnlyDictionary<string, byte[]> Faces => new Dictionary<string, byte[]>(_faces);

    /// <summary>
    /// Off the network: every connection is dropped before an answer, as when a reader is
    /// unplugged mid-call (ISA-14).
    /// </summary>
    public bool Offline
    {
        get => _offline;
        set => _offline = value;
    }

    public static async Task<StubReader> StartAsync(string password)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        var certificate = SelfSignedCertificate();
        builder.WebHost.UseKestrelCore().ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.UseHttps(certificate))
        );

        var app = builder.Build();
        var reader = new StubReader(app, password, certificate);
        app.Run(reader.HandleAsync);

        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.Single();
        reader.Port = new Uri(address).Port;
        return reader;
    }

    /// <summary>Refuses every later <paramref name="step"/> as a record it will not take (ISA-13).</summary>
    public void Refuse(IsapiStep step, string subStatusCode) =>
        _scripts[step] = context => AnswerAsync(context, HttpStatusCode.BadRequest, Refusal(subStatusCode));

    /// <summary>Answers every later <paramref name="step"/> with a bare <paramref name="status"/>.</summary>
    public void Answer(IsapiStep step, HttpStatusCode status) =>
        _scripts[step] = context =>
        {
            context.Response.StatusCode = (int)status;
            return Task.CompletedTask;
        };

    /// <summary>Back to answering <paramref name="step"/> as a working reader would.</summary>
    public void Behave(IsapiStep step) => _scripts.TryRemove(step, out _);

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        Certificate.Dispose();
    }

    private async Task HandleAsync(HttpContext context)
    {
        if (_offline)
        {
            context.Abort();
            return;
        }

        // Read in full before answering, even a challenge, so the client is never cut off
        // mid-upload by a response it has not finished sending a body for.
        using var buffer = new MemoryStream();
        await context.Request.Body.CopyToAsync(buffer);
        var body = buffer.ToArray();

        if (!Authenticated(context))
        {
            Challenge(context);
            return;
        }

        if (StepOf(context.Request) is not { } step)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        if (_scripts.TryGetValue(step, out var script))
        {
            await script(context);
            return;
        }

        var refusal = step switch
        {
            IsapiStep.PersonCreate => CreatePerson(body),
            IsapiStep.PersonModify => ModifyPerson(body),
            IsapiStep.PersonDelete => DeletePersons(body),
            IsapiStep.FaceDelete => DeleteFaces(context.Request, body),
            IsapiStep.FaceCreate => await CreateFaceAsync(context.Request, body),
            _ => null,
        };

        await (
            refusal is null
                ? AnswerAsync(context, HttpStatusCode.OK, """{"statusCode":1,"statusString":"OK","subStatusCode":"ok"}""")
                : AnswerAsync(context, HttpStatusCode.BadRequest, Refusal(refusal))
        );
    }

    // ─── Digest, RFC 7616: MD5, qop="auth" ───

    private bool Authenticated(HttpContext context)
    {
        string authorization = context.Request.Headers.Authorization.ToString();
        if (!authorization.StartsWith("Digest ", StringComparison.Ordinal))
            return false;

        var fields = DigestFields()
            .Matches(authorization["Digest ".Length..])
            .ToDictionary(
                match => match.Groups["key"].Value,
                match => match.Groups["quoted"].Success ? match.Groups["quoted"].Value : match.Groups["token"].Value,
                StringComparer.OrdinalIgnoreCase
            );

        var target = context.Features.GetRequiredFeature<IHttpRequestFeature>().RawTarget;
        var valid =
            fields.GetValueOrDefault("username") == Username
            && fields.GetValueOrDefault("realm") == Realm
            && fields.GetValueOrDefault("qop") == "auth"
            && fields.GetValueOrDefault("uri") == target
            && fields.TryGetValue("nonce", out var nonce)
            && _nonces.ContainsKey(nonce)
            && fields.TryGetValue("nc", out var count)
            && fields.TryGetValue("cnonce", out var clientNonce)
            && fields.TryGetValue("response", out var response)
            && string.Equals(
                response,
                Md5($"{Md5($"{Username}:{Realm}:{_password}")}:{nonce}:{count}:{clientNonce}:auth:{Md5($"{context.Request.Method}:{target}")}"),
                StringComparison.OrdinalIgnoreCase
            );

        if (!valid)
            Interlocked.Increment(ref _failedLogins);

        return valid;
    }

    private void Challenge(HttpContext context)
    {
        var nonce = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        _nonces[nonce] = 0;

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate =
            $"Digest realm=\"{Realm}\", qop=\"auth\", nonce=\"{nonce}\", opaque=\"{Md5(Realm)}\", algorithm=MD5";
    }

    [SuppressMessage(
        "Security",
        "CA5351:Do Not Use Broken Cryptographic Algorithms",
        Justification = "MD5 is the digest algorithm the reader challenges with (RFC 7616); this is the reader's side of that exchange, in a test."
    )]
    private static string Md5(string value) =>
        Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(value)));

    [GeneratedRegex("""(?<key>\w+)=(?:"(?<quoted>[^"]*)"|(?<token>[^,\s]*))""")]
    private static partial Regex DigestFields();

    // ─── The five ISAPI calls the adapter makes ───

    private static IsapiStep? StepOf(HttpRequest request) =>
        (request.Method, request.Path.Value) switch
        {
            ("POST", "/ISAPI/AccessControl/UserInfo/Record") => IsapiStep.PersonCreate,
            ("PUT", "/ISAPI/AccessControl/UserInfo/Modify") => IsapiStep.PersonModify,
            ("PUT", "/ISAPI/AccessControl/UserInfo/Delete") => IsapiStep.PersonDelete,
            ("PUT", "/ISAPI/Intelligent/FDLib/FDSearch/Delete") => IsapiStep.FaceDelete,
            ("POST", "/ISAPI/Intelligent/FDLib/FaceDataRecord") => IsapiStep.FaceCreate,
            _ => null,
        };

    private string? CreatePerson(byte[] body)
    {
        var person = PersonIn(body);
        return _persons.TryAdd(person.EmployeeNo, person) ? null : PersonAlreadyThere;
    }

    private string? ModifyPerson(byte[] body)
    {
        var person = PersonIn(body);
        if (!_persons.ContainsKey(person.EmployeeNo))
            return NothingThere;

        _persons[person.EmployeeNo] = person;
        return null;
    }

    private string? DeletePersons(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var removed = document
            .RootElement.GetProperty("UserInfoDelCond")
            .GetProperty("EmployeeNoList")
            .EnumerateArray()
            .Count(entry => _persons.TryRemove(entry.GetProperty("employeeNo").GetString()!, out _));

        return removed > 0 ? null : NothingThere;
    }

    private string? DeleteFaces(HttpRequest request, byte[] body)
    {
        if (request.Query["FDID"] != FaceLibraryId || request.Query["faceLibType"] != FaceLibraryType)
            return "badParameters";

        using var document = JsonDocument.Parse(body);
        var removed = document
            .RootElement.GetProperty("FPID")
            .EnumerateArray()
            .Count(entry => _faces.TryRemove(entry.GetProperty("value").GetString()!, out _));

        return removed > 0 ? null : NothingThere;
    }

    /// <summary>Two parts, in this order or not at all: the record, then the JPEG (A-1).</summary>
    private async Task<string?> CreateFaceAsync(HttpRequest request, byte[] body)
    {
        var boundary = HeaderUtilities
            .RemoveQuotes(MediaTypeHeaderValue.Parse(request.ContentType).Boundary)
            .Value!;
        var multipart = new MultipartReader(boundary, new MemoryStream(body));

        // A section's body is readable only until the next section is asked for.
        var record = await multipart.ReadNextSectionAsync();
        if (record is null || NameOf(record) != "FaceDataRecord")
            return "badParameters";

        using var document = await JsonDocument.ParseAsync(record.Body);
        var fields = document.RootElement;

        var image = await multipart.ReadNextSectionAsync();
        if (image is null || NameOf(image) != "FaceImage" || image.ContentType != "image/jpeg")
            return "badParameters";

        using var bytes = new MemoryStream();
        await image.Body.CopyToAsync(bytes);

        if (
            await multipart.ReadNextSectionAsync() is not null
            || fields.GetProperty("FDID").GetString() != FaceLibraryId
            || fields.GetProperty("faceLibType").GetString() != FaceLibraryType
        )
            return "badParameters";

        return _faces.TryAdd(fields.GetProperty("FPID").GetString()!, bytes.ToArray())
            ? null
            : FaceAlreadyThere;
    }

    private static string? NameOf(MultipartSection section) =>
        ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
            ? HeaderUtilities.RemoveQuotes(disposition.Name).Value
            : null;

    private static StubPerson PersonIn(byte[] body)
    {
        using var document = JsonDocument.Parse(body);
        var person = document.RootElement.GetProperty("UserInfo");
        return new StubPerson(
            person.GetProperty("employeeNo").GetString()!,
            person.GetProperty("name").GetString()!,
            person.GetProperty("password").GetString()!
        );
    }

    private static string Refusal(string subStatusCode) =>
        JsonSerializer.Serialize(
            new
            {
                statusCode = 6,
                statusString = "Invalid Content",
                subStatusCode,
            }
        );

    private static Task AnswerAsync(HttpContext context, HttpStatusCode status, string json)
    {
        context.Response.StatusCode = (int)status;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(json);
    }

    /// <summary>
    /// Re-imported from PKCS #12 so the private key is one the platform's TLS stack can use: an
    /// ephemeral key straight from <c>CreateSelfSigned</c> is refused by SslStream on some
    /// platforms.
    /// </summary>
    private static X509Certificate2 SelfSignedCertificate()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension([new Oid("1.3.6.1.5.5.7.3.1")], critical: false)
        );

        using var created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1)
        );
        return X509CertificateLoader.LoadPkcs12(created.Export(X509ContentType.Pfx), password: null);
    }
}

/// <summary>A person as the stub reader holds it: its key, name and PIN.</summary>
public sealed record StubPerson(string EmployeeNo, string Name, string Pin);
