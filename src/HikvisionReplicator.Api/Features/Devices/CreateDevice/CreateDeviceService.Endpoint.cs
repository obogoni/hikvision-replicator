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
        app.MapPost("/api/devices", (CreateDeviceRequest req, ICreateDeviceService svc) =>
            svc.ExecuteAsync(req));
        return app;
    }
}
