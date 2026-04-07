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
        app.MapDelete("/api/devices/{id:int}", (int id, IDeleteDeviceService svc) =>
            svc.ExecuteAsync(id));
        return app;
    }
}
