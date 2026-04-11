using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Devices.CreateDevice;

public static class CreateDeviceServiceEndpoint
{
    public static WebApplicationBuilder UseCreateDevice(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ICreateDeviceService, CreateDeviceService>();
        return builder;
    }

    public static WebApplication MapCreateDevice(this WebApplication app)
    {
        app.MapPost("/api/devices", async (CreateDeviceRequest req, ICreateDeviceService svc, CancellationToken ct) =>
        {
            var result = await svc.ExecuteAsync(req, ct);
            return result.Match(
                response => Results.Created($"/api/devices/{response.Id}", response),
                validationError => validationError.ToMinimalApiResult(),
                conflictError => conflictError.ToMinimalApiResult());
        });
        return app;
    }
}
