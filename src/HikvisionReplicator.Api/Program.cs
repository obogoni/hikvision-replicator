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

builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();

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

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();

public partial class Program { }
