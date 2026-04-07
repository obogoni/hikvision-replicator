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
        app.MapPut("/api/devices/{id:int}", (int id, UpdateDeviceRequest req, IUpdateDeviceService svc) =>
            svc.ExecuteAsync(id, req));
        return app;
    }
}
