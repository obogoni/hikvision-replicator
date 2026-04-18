using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Users.GetUser;

public static class GetUserServiceEndpoint
{
    public static WebApplicationBuilder UseGetUser(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGetUserService, GetUserService>();
        return builder;
    }

    public static WebApplication MapGetUser(this WebApplication app)
    {
        app.MapGet(
                "/api/users/{id:int}",
                async (int id, IGetUserService svc, CancellationToken ct) =>
                {
                    var result = await svc.ExecuteAsync(id, ct);
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
