using HikvisionReplicator.Api.Domain.Events;
using HikvisionReplicator.Api.Features.Devices.GetDevice;
using HikvisionReplicator.Api.Features.Devices.ListDevices;
using HikvisionReplicator.Api.Features.Devices.RegisterDevice;
using HikvisionReplicator.Api.Features.Devices.RemoveDevice;
using HikvisionReplicator.Api.Features.Devices.UpdateDevice;
using HikvisionReplicator.Api.Features.Users.GetUser;
using HikvisionReplicator.Api.Features.Users.ListUsers;
using HikvisionReplicator.Api.Features.Users.RemoveUser;
using HikvisionReplicator.Api.Features.Users.UpsertUser;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
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
// Stateless and CPU-bound, so one instance serves every request (A-14).
builder.Services.AddSingleton<IFaceImageNormalizer, SkiaFaceImageNormalizer>();
// One set of instruments for the process, published on a meter the factory owns.
builder.Services.AddSingleton<ReplicationMetrics>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IReplicationRepository, ReplicationRepository>();

// The fan-out is one object serving five events, so it is registered once and surfaced
// under each handler interface — a separate instance per interface would read the device
// catalogue once per event on a path AD-038 measures. The concrete type stays resolvable
// because the backfill expansion is invoked directly, never through an event (AD-039).
builder.Services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
builder.Services.AddScoped<ReplicationFanOut>();
builder.Services.AddScoped<IDomainEventHandler<UserRegistered>>(services =>
    services.GetRequiredService<ReplicationFanOut>()
);
builder.Services.AddScoped<IDomainEventHandler<UserRestored>>(services =>
    services.GetRequiredService<ReplicationFanOut>()
);
builder.Services.AddScoped<IDomainEventHandler<UserChanged>>(services =>
    services.GetRequiredService<ReplicationFanOut>()
);
builder.Services.AddScoped<IDomainEventHandler<UserRemoved>>(services =>
    services.GetRequiredService<ReplicationFanOut>()
);
builder.Services.AddScoped<IDomainEventHandler<DeviceRegistered>>(services =>
    services.GetRequiredService<ReplicationFanOut>()
);

builder.UseRegisterDevice();
builder.UseGetDevice();
builder.UseListDevices();
builder.UseUpdateDevice();
builder.UseRemoveDevice();

builder.UseUpsertUser();
builder.UseGetUser();
builder.UseListUsers();
builder.UseRemoveUser();

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
                // Without this the normalizer's ActivitySource has no listener and emits
                // nothing at all — the child span USR-40 requires would silently not exist.
                .AddSource(SkiaFaceImageNormalizer.ActivitySourceName)
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(otlpEndpoint);
                    options.Protocol = OtlpExportProtocol.Grpc;
                })
        )
        // USR-41. An instrument with no reader records into nothing: the normalizer's
        // histograms existed and were unit-observable while production collected neither.
        // Normalization is the only CPU-bound step on the write path AD-014 makes the primary
        // quality attribute, so leaving it unexported is leaving that attribute unmeasured.
        .WithMetrics(metrics =>
            metrics
                .AddAspNetCoreInstrumentation()
                .AddMeter(SkiaFaceImageNormalizer.MeterName)
                // REP-39, the same lesson one feature later: without this line every
                // instrument in ReplicationMetrics records into nothing in production while
                // a test that installs its own listener goes on passing (L-037).
                .AddMeter(ReplicationMetrics.MeterName)
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

    // L-037's shape, one level up. An event with no registered handler fans out nothing and
    // throws nothing: the write succeeds, the spectator is queued nowhere, and the first
    // symptom is a face that does not open a turnstile. A missing registration is a startup
    // defect, so this is where it stops the process.
    var unhandled = typeof(IDomainEvent)
        .Assembly.GetTypes()
        .Where(type =>
            type is { IsAbstract: false, IsInterface: false }
            && typeof(IDomainEvent).IsAssignableFrom(type)
        )
        .Where(eventType =>
            !scope
                .ServiceProvider.GetServices(
                    typeof(IDomainEventHandler<>).MakeGenericType(eventType)
                )
                .Any()
        )
        .Select(eventType => eventType.Name)
        .ToList();

    if (unhandled.Count > 0)
    {
        throw new InvalidOperationException(
            $"{Program.UnhandledDomainEvents} {string.Join(", ", unhandled)}"
        );
    }
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

app.MapUpsertUser();
app.MapGetUser();
app.MapListUsers();
app.MapRemoveUser();

app.Run();

public partial class Program
{
    /// <summary>
    /// What startup says when an event would reach nobody. Named so the assertion that
    /// proves the refusal does not have to match on a sentence fragment.
    /// </summary>
    public const string UnhandledDomainEvents =
        "No handler is registered for these domain events, so a write raising one would queue nothing:";
}
