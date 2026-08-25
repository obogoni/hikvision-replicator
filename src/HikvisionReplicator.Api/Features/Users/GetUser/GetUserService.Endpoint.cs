using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Users.GetUser;

public static class GetUserServiceEndpoint
{
    public static WebApplicationBuilder UseGetUser(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IGetUserService, GetUserService>();
        return builder;
    }

    public static WebApplication MapGetUser(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
                "/api/users/{externalRef}",
                async (string externalRef, IGetUserService service, CancellationToken ct) =>
                {
                    var result = await service.ExecuteAsync(externalRef, ct);
                    return result.Match(
                        response => Results.Ok(response),
                        notFoundError => notFoundError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Users");

        return app;
    }
}
