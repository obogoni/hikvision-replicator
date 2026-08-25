namespace HikvisionReplicator.Api.Features.Users.ListUsers;

public static class ListUsersServiceEndpoint
{
    public static WebApplicationBuilder UseListUsers(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddScoped<IListUsersService, ListUsersService>();
        return builder;
    }

    public static WebApplication MapListUsers(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapGet(
                "/api/users",
                async (
                    int? page,
                    int? pageSize,
                    IListUsersService service,
                    CancellationToken ct
                ) => Results.Ok(await service.ExecuteAsync(page, pageSize, ct))
            )
            .WithTags("Users");

        return app;
    }
}
