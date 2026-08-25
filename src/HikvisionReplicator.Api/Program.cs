using HikvisionReplicator.Api.Features.Devices.GetDevice;
using HikvisionReplicator.Api.Features.Devices.ListDevices;
using HikvisionReplicator.Api.Features.Devices.RegisterDevice;
using HikvisionReplicator.Api.Features.Devices.RemoveDevice;
using HikvisionReplicator.Api.Features.Devices.UpdateDevice;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// A missing or malformed key aborts startup rather than failing on first use (DEV-15).
builder.Services.AddSingleton<IValidateOptions<EncryptionOptions>, EncryptionOptionsValidator>();
builder
    .Services.AddOptions<EncryptionOptions>()
    .Bind(builder.Configuration.GetSection(EncryptionOptions.SectionName))
    .ValidateOnStart();

// A-13's envelope is configuration, not constants: Phase 3 must be able to correct it against
// real hardware without a code change. A bound that cannot be satisfied aborts startup rather
// than failing on the first upload, by which point a spectator is already at the turnstile.
builder.Services.AddSingleton<IValidateOptions<FaceImageOptions>, FaceImageOptionsValidator>();
builder
    .Services.AddOptions<FaceImageOptions>()
    .Bind(builder.Configuration.GetSection(FaceImageOptions.SectionName))
    .ValidateOnStart();

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

builder.UseRegisterDevice();
builder.UseGetDevice();
builder.UseListDevices();
builder.UseUpdateDevice();
builder.UseRemoveDevice();

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

// Database failures become 503 and everything else 500, always as a problem body
// and never carrying the exception itself (DEV-14).
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Tracing is exported only when an endpoint is configured (DEV-16). EF instrumentation
// is left at its defaults so SQL text — and therefore parameters — is never captured,
// and sensitive data logging is never enabled (DEV-07).
var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder
        .Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(serviceName: "hikvision-replicator"))
        .WithTracing(tracing =>
            tracing
                .AddAspNetCoreInstrumentation()
                .AddEntityFrameworkCoreInstrumentation()
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                })
        );
}

var app = builder.Build();

// Migrations are the only schema authority; the schema is never created
// implicitly, so migration history always matches the database (DEV-12).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.UseExceptionHandler();

// Gives an RFC 7807 body to framework-generated bodiless failures — a malformed JSON
// body is answered as a 400 problem, never an empty response or a 500.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapRegisterDevice();
app.MapGetDevice();
app.MapListDevices();
app.MapUpdateDevice();
app.MapRemoveDevice();

app.Run();

public partial class Program { }
