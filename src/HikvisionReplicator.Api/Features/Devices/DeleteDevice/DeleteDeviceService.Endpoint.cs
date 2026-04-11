using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Devices.DeleteDevice;

public static class DeleteDeviceServiceEndpoint
{
    public static WebApplicationBuilder UseDeleteDevice(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IDeleteDeviceService, DeleteDeviceService>();
        return builder;
    }

    public static WebApplication MapDeleteDevice(this WebApplication app)
    {
        app.MapDelete(
            "/api/devices/{id:int}",
            async (int id, IDeleteDeviceService svc, CancellationToken ct) =>
            {
                var result = await svc.ExecuteAsync(id, ct);
                return result.Match(
                    _ => Results.NoContent(),
                    notFoundError => notFoundError.ToMinimalApiResult()
                );
            }
        );
        return app;
    }
}
