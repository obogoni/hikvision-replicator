using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Devices.RemoveDevice;

public static class RemoveDeviceServiceEndpoint
{
    public static WebApplicationBuilder UseRemoveDevice(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IRemoveDeviceService, RemoveDeviceService>();
        return builder;
    }

    public static WebApplication MapRemoveDevice(this WebApplication app)
    {
        app.MapDelete(
                "/api/devices/{id:int}",
                async (int id, IRemoveDeviceService service, CancellationToken ct) =>
                {
                    var result = await service.ExecuteAsync(id, ct);
                    return result.Match(
                        success => Results.NoContent(),
                        notFoundError => notFoundError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Devices");

        return app;
    }
}
