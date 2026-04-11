using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Devices.GetDevice;

public static class GetDeviceServiceEndpoint
{
    public static WebApplicationBuilder UseGetDevice(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGetDeviceService, GetDeviceService>();
        return builder;
    }

    public static WebApplication MapGetDevice(this WebApplication app)
    {
        app.MapGet(
                "/api/devices/{id:int}",
                async (int id, IGetDeviceService svc, CancellationToken ct) =>
                {
                    var result = await svc.ExecuteAsync(id, ct);
                    return result.Match(
                        response => Results.Ok(response),
                        notFoundError => notFoundError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Devices");
        return app;
    }
}
