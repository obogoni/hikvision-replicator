using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

public static class UpsertUserServiceEndpoint
{
    public static WebApplicationBuilder UseUpsertUser(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IUpsertUserService, UpsertUserService>();
        return builder;
    }

    /// <summary>
    /// Room for the rest of the representation once the picture is accounted for: the name, the
    /// access code, the field names and the punctuation around them.
    /// </summary>
    private const long JsonEnvelopeAllowance = 4 * 1024;

    public static WebApplication MapUpsertUser(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var faceImage = app.Services.GetRequiredService<IOptions<FaceImageOptions>>().Value;

        app.MapPut(
                "/api/users/{externalRef}",
                async (
                    string externalRef,
                    UpsertUserRequest request,
                    IUpsertUserService service,
                    CancellationToken ct
                ) =>
                {
                    var result = await service.ExecuteAsync(externalRef, request, ct);

                    // Two success arms exist precisely so this maps straight to 201 and 200:
                    // there is no `if` in the transport layer (AD-003).
                    return result.Match(
                        created => Results.Created(LocationOf(created.User.ExternalRef), created.User),
                        updated => Results.Ok(updated.User),
                        validationError => validationError.ToMinimalApiResult(),
                        conflictError => conflictError.ToMinimalApiResult()
                    );
                }
            )
            // USR-19 at the transport layer. The normalizer's own cap only refuses an upload it
            // has already been handed, by which point the whole body sits in memory — this is the
            // line that refuses to read it at all, and A-11 makes that the only bound on what an
            // unauthenticated caller can make this endpoint allocate.
            .WithMetadata(new RequestSizeLimitAttribute(MaxRequestBytes(faceImage)))
            .WithTags("Users");

        return app;
    }

    /// <summary>
    /// The largest body a valid upload can arrive in, which is <b>not</b>
    /// <see cref="FaceImageOptions.MaxUploadBytes"/>.
    /// <para>
    /// A-9 sends the picture as base64 inside JSON, so three bytes of image become four
    /// characters on the wire: an 8 MB photograph is ~10.7 MB of request. A limit set to the
    /// image cap itself would refuse every upload the normalizer would have accepted.
    /// </para>
    /// </summary>
    private static long MaxRequestBytes(FaceImageOptions faceImage)
    {
        var encoded = ((long)faceImage.MaxUploadBytes + 2) / 3 * 4;
        return encoded + JsonEnvelopeAllowance;
    }

    /// <summary>
    /// The URL the caller just addressed (USR-01). The reference is an opaque integrator key that
    /// may contain characters with meaning in a URL, so it is escaped rather than concatenated.
    /// </summary>
    private static string LocationOf(string externalRef) =>
        $"/api/users/{Uri.EscapeDataString(externalRef)}";
}
