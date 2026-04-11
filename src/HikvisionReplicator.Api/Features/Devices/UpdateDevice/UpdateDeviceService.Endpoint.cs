using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Devices.UpdateDevice;

public static class UpdateDeviceServiceEndpoint
{
    public static WebApplicationBuilder UseUpdateDevice(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IUpdateDeviceService, UpdateDeviceService>();
        return builder;
    }

    public static WebApplication MapUpdateDevice(this WebApplication app)
    {
        app.MapPut(
            "/api/devices/{id:int}",
            async (
                int id,
                UpdateDeviceRequest req,
                IUpdateDeviceService svc,
                CancellationToken ct
            ) =>
            {
                var result = await svc.ExecuteAsync(id, req, ct);
                return result.Match(
                    response => Results.Ok(response),
                    validationError => validationError.ToMinimalApiResult(),
                    notFoundError => notFoundError.ToMinimalApiResult(),
                    conflictError => conflictError.ToMinimalApiResult()
                );
            }
        );
        return app;
    }
}
