using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Users.UpsertUser;

public static class UpsertUserServiceEndpoint
{
    public static WebApplicationBuilder UseUpsertUser(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUpsertUserService, UpsertUserService>();
        return builder;
    }

    public static WebApplication MapUpsertUser(this WebApplication app)
    {
        app.MapPost(
                "/api/users",
                async (UpsertUserRequest req, IUpsertUserService svc, CancellationToken ct) =>
                {
                    var result = await svc.ExecuteAsync(req, ct);
                    return result.Match(
                        upsertResult => upsertResult.WasCreated
                            ? Results.Created($"/api/users/{upsertResult.User.Id}", upsertResult.User)
                            : Results.Ok(upsertResult.User),
                        validationError => validationError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Users");
        return app;
    }
}
