using HikvisionReplicator.Api.Infrastructure;

namespace HikvisionReplicator.Api.Features.Devices.RegisterDevice;

public static class RegisterDeviceServiceEndpoint
{
    public static WebApplicationBuilder UseRegisterDevice(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IRegisterDeviceService, RegisterDeviceService>();
        return builder;
    }

    public static WebApplication MapRegisterDevice(this WebApplication app)
    {
        app.MapPost(
                "/api/devices",
                async (
                    RegisterDeviceRequest request,
                    IRegisterDeviceService service,
                    CancellationToken ct
                ) =>
                {
                    var result = await service.ExecuteAsync(request, ct);
                    return result.Match(
                        response => Results.Created($"/api/devices/{response.Id}", response),
                        validationError => validationError.ToMinimalApiResult(),
                        conflictError => conflictError.ToMinimalApiResult()
                    );
                }
            )
            .WithTags("Devices");

        return app;
    }
}
