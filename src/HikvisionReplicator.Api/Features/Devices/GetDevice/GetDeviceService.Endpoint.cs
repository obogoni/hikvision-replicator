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
        app.MapGet("/api/devices/{id:int}", (int id, IGetDeviceService svc) =>
            svc.ExecuteAsync(id));
        return app;
    }
}
