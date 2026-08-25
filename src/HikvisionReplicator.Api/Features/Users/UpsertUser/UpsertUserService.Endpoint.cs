using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

public static class UpsertUserServiceEndpoint
{
    public static WebApplicationBuilder UseUpsertUser(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IUpsertUserService, UpsertUserService>();
        return builder;
    }

    public static WebApplication MapUpsertUser(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

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
            .WithTags("Users");

        return app;
    }

    /// <summary>
    /// The URL the caller just addressed (USR-01). The reference is an opaque integrator key that
    /// may contain characters with meaning in a URL, so it is escaped rather than concatenated.
    /// </summary>
    private static string LocationOf(string externalRef) =>
        $"/api/users/{Uri.EscapeDataString(externalRef)}";
}
