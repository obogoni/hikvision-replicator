using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Users.RemoveUser;

public static class RemoveUserServiceEndpoint
{
    public static WebApplicationBuilder UseRemoveUser(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IRemoveUserService, RemoveUserService>();
        return builder;
    }

    public static WebApplication MapRemoveUser(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapDelete(
                "/api/users/{externalRef}",
                async (string externalRef, IRemoveUserService service, CancellationToken ct) =>
                {
                    var result = await service.ExecuteAsync(externalRef, ct);
                    return result.Match(
                        success => Results.NoContent(),
                        notFoundError => notFoundError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Users");

        return app;
    }
}
