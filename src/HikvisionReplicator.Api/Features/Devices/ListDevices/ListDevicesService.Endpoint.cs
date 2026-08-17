namespace HikvisionReplicator.Api.Features.Devices.ListDevices;

public static class ListDevicesServiceEndpoint
{
    public static WebApplicationBuilder UseListDevices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IListDevicesService, ListDevicesService>();
        return builder;
    }

    public static WebApplication MapListDevices(this WebApplication app)
    {
        app.MapGet(
                "/api/devices",
                async (IListDevicesService service, CancellationToken ct) =>
                    Results.Ok(await service.ExecuteAsync(ct))
            )
            .WithTags("Devices");

        return app;
    }
}
