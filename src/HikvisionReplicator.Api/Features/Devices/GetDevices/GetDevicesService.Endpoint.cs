namespace HikvisionReplicator.Api.Features.Devices.GetDevices;

public static class GetDevicesServiceEndpoint
{
    public static WebApplicationBuilder UseGetDevices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IGetDevicesService, GetDevicesService>();
        return builder;
    }

    public static WebApplication MapGetDevices(this WebApplication app)
    {
        app.MapGet(
                "/api/devices",
                async (IGetDevicesService svc, CancellationToken ct) =>
                    Results.Ok(await svc.ExecuteAsync(ct))
            )
            .WithTags("Devices");
        return app;
    }
}
