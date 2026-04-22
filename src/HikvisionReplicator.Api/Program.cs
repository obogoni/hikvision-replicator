using Hangfire;
using Hangfire.Storage.SQLite;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Features.Devices.CreateDevice;
using HikvisionReplicator.Api.Features.Devices.DeleteDevice;
using HikvisionReplicator.Api.Features.Devices.GetDevice;
using HikvisionReplicator.Api.Features.Devices.GetDevices;
using HikvisionReplicator.Api.Features.Devices.UpdateDevice;
using HikvisionReplicator.Api.Features.Users.UpsertUser;
using HikvisionReplicator.Api.Features.Users.GetUser;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection"))
);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
if (!string.IsNullOrEmpty(otlpEndpoint))
{
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(serviceName: "hikvision-replicator"))
        .WithTracing(tracing => tracing
            .AddAspNetCoreInstrumentation()
            .AddEntityFrameworkCoreInstrumentation()
            .AddOtlpExporter(options =>
            {
                options.Endpoint = new Uri(otlpEndpoint);
                options.Protocol = OtlpExportProtocol.Grpc;
            }));
}
builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IRepository<Device>, DeviceRepository>();
builder.Services.AddScoped<IRepository<User>, UserRepository>();
builder.Services.AddScoped<IRepository<Replication>, ReplicationRepository>();

builder
    .UseCreateDevice()
    .UseGetDevice()
    .UseGetDevices()
    .UseUpdateDevice()
    .UseDeleteDevice()
    .UseUpsertUser()
    .UseGetUser();

builder.Services.AddHangfire(config => config
    .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
    .UseSimpleAssemblyNameTypeSerializer()
    .UseRecommendedSerializerSettings()
    .UseSQLiteStorage(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfireServer();

var app = builder.Build();

app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
    app.UseHangfireDashboard("/hangfire");
}

app.MapCreateDevice()
    .MapGetDevices()
    .MapGetDevice()
    .MapUpdateDevice()
    .MapDeleteDevice()
    .MapUpsertUser()
    .MapGetUser();

app.Run();

public partial class Program { }
