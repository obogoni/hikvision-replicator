using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Users.CreateUser;

public static class CreateUserServiceEndpoint
{
    public static WebApplicationBuilder UseCreateUser(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ICreateUserService, CreateUserService>();
        return builder;
    }

    public static WebApplication MapCreateUser(this WebApplication app)
    {
        app.MapPost(
                "/api/users",
                async (CreateUserRequest req, ICreateUserService svc, CancellationToken ct) =>
                {
                    var result = await svc.ExecuteAsync(req, ct);
                    return result.Match(
                        response => Results.Created($"/api/users/{response.Id}", response),
                        validationError => validationError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Users");
        return app;
    }
}
