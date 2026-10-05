using System.Net;
using HikvisionReplicator.Api.Domain.Events;
using HikvisionReplicator.Api.Infrastructure;
using HikvisionReplicator.Api.Shared;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The behaviours that are decided while the application is starting, not while it is
/// handling a request: encryption-key validation (DEV-15), conditional tracing (DEV-16),
/// the Development-only API documentation (DEV-17), the wiring the write path fans out
/// through (AD-042), and what the assembly deliberately does <em>not</em> contain (REP-34,
/// AD-039).
/// </summary>
[Collection(PostgresCollection.Name)]
public class StartupTests(PostgresFixture fixture)
{
    private const string OpenApiDocumentPath = "/openapi/v1.json";
    private const string ApiReferencePath = "/scalar/v1";
    private const string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";

    /// <summary>A three-byte key — valid Base64, but not the 32 bytes AES-256 needs.</summary>
    private const string ThreeByteBase64Key = "AAAA";

    /// <summary>
    /// The runners and schedulers AD-039 defers to feature 4. OD-3 is still open, so this is
    /// the field of candidates rather than one rejected product.
    /// </summary>
    private static readonly string[] JobRunners =
    [
        "Hangfire",
        "Quartz",
        "Coravel",
        "FluentScheduler",
        "NCrontab",
        "Cronos",
        "Rebus",
        "MassTransit",
        "Silverback",
    ];

    private WebApplicationFactory<Program> BootWith(
        string? environment = null,
        params (string Key, string? Value)[] settings
    ) =>
        fixture.Factory.WithWebHostBuilder(builder =>
        {
            if (environment is not null)
                builder.UseEnvironment(environment);

            // Both layers are needed. Host settings are the only ones in place early
            // enough for the values Program.cs reads while assembling the builder (the
            // OTLP endpoint); the app-configuration source is the only one that outranks
            // the harness defaults the base factory supplies (the encryption key).
            foreach (var (key, value) in settings)
                builder.UseSetting(key, value);

            builder.ConfigureAppConfiguration(configuration =>
                configuration.AddInMemoryCollection(
                    settings.ToDictionary(setting => setting.Key, setting => setting.Value)
                )
            );
        });

    // ─── DEV-15: a bad encryption key stops the application from starting ─
    // CreateClient() builds and starts the host. A throw here means the process never
    // reached the point of serving a request.

    [Fact]
    public void Application_does_not_start_without_an_encryption_key()
    {
        using var factory = BootWith(settings: (EncryptionOptionsValidator.KeyPath, null));

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(EncryptionOptionsValidator.KeyPath, exception.Message);
        Assert.Contains(EncryptionOptionsValidator.MissingKeyMessage, exception.Message);
    }

    [Fact]
    public void Application_does_not_start_with_a_key_of_the_wrong_length()
    {
        using var factory = BootWith(
            settings: (EncryptionOptionsValidator.KeyPath, ThreeByteBase64Key)
        );

        var exception = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());

        Assert.Contains(EncryptionOptionsValidator.KeyPath, exception.Message);
        Assert.Contains(EncryptionOptionsValidator.WrongLengthKeyMessage, exception.Message);
    }

    // ─── DEV-16: tracing exists only when an export endpoint is configured ─

    [Fact]
    public void Traces_are_not_collected_without_a_configured_export_endpoint()
    {
        using var factory = BootWith(settings: (OtlpEndpointKey, ""));

        Assert.Null(factory.Services.GetService<TracerProvider>());
    }

    [Fact]
    public void Traces_are_collected_when_an_export_endpoint_is_configured()
    {
        using var factory = BootWith(settings: (OtlpEndpointKey, "http://localhost:4317"));

        Assert.NotNull(factory.Services.GetService<TracerProvider>());
    }

    // ─── DEV-17: the API documentation is a Development-only surface ───────

    [Fact]
    public async Task Api_description_is_published_during_development()
    {
        using var factory = BootWith(environment: Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(OpenApiDocumentPath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_reference_is_published_during_development()
    {
        using var factory = BootWith(environment: Environments.Development);
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ApiReferencePath);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Api_description_is_withheld_outside_development()
    {
        using var factory = BootWith();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(OpenApiDocumentPath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Api_reference_is_withheld_outside_development()
    {
        using var factory = BootWith();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(ApiReferencePath);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ─── AD-042: the write path can only fan out through what is registered ─

    [Fact]
    public void Application_resolves_the_queue_wiring_a_user_write_fans_out_through()
    {
        using var factory = BootWith();
        using var scope = factory.Services.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<IReplicationRepository>());
        Assert.NotNull(scope.ServiceProvider.GetService<IDomainEventDispatcher>());
    }

    [Fact]
    public void Every_event_an_aggregate_can_raise_reaches_a_registered_handler()
    {
        using var factory = BootWith();
        using var scope = factory.Services.CreateScope();

        // Listed rather than discovered by reflection: production discovers them that way,
        // and a test that repeats the discovery would agree with a broken scan.
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IDomainEventHandler<UserRegistered>>());
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IDomainEventHandler<UserRestored>>());
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IDomainEventHandler<UserChanged>>());
        Assert.NotEmpty(scope.ServiceProvider.GetServices<IDomainEventHandler<UserRemoved>>());
        Assert.NotEmpty(
            scope.ServiceProvider.GetServices<IDomainEventHandler<DeviceRegistered>>()
        );
    }

    [Fact]
    public void Application_does_not_start_when_an_event_would_reach_nobody()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services =>
                services.RemoveAll<IDomainEventHandler<UserRemoved>>()
            )
        );

        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());

        // The literal sentence is pinned here and nowhere else. Comparing only against the
        // constant would prove the right branch ran, not that the message says anything —
        // edit the constant and assertion and implementation move together, green all the
        // way (AD-036's tautology trap).
        Assert.Contains(
            "No handler is registered for these domain events",
            exception.Message,
            StringComparison.Ordinal
        );
        Assert.Contains(nameof(UserRemoved), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(UserRegistered), exception.Message, StringComparison.Ordinal);
    }

    // ─── REP-34 / AD-039: this feature ships nothing that runs ───────────

    [Fact]
    public void Application_ships_nothing_that_runs_on_its_own()
    {
        var assembly = typeof(AppDbContext).Assembly;

        Assert.DoesNotContain(
            assembly.GetTypes(),
            type => typeof(IHostedService).IsAssignableFrom(type)
        );
        Assert.DoesNotContain(
            assembly.GetTypes(),
            type => typeof(BackgroundService).IsAssignableFrom(type)
        );

        // A hosted service could also arrive by registration alone, without a type of ours.
        // The framework registers its own, so only this assembly's are in scope.
        using var factory = BootWith();
        Assert.DoesNotContain(
            factory.Services.GetServices<IHostedService>(),
            service => service.GetType().Assembly == assembly
        );
    }

    [Fact]
    public void Application_carries_no_job_runner_or_scheduler()
    {
        // Two sweeps, because neither alone is enough. Compiled references name only what
        // the code actually touches, so an unused package would not appear there; the
        // output directory names everything that ships, including a package pulled in and
        // not yet called.
        var referenced = typeof(AppDbContext)
            .Assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty);
        var shipped = Directory
            .EnumerateFiles(AppContext.BaseDirectory, "*.dll")
            .Select(path => Path.GetFileNameWithoutExtension(path) ?? string.Empty);

        foreach (var name in referenced.Concat(shipped))
        {
            Assert.DoesNotContain(
                JobRunners,
                runner => name.Contains(runner, StringComparison.OrdinalIgnoreCase)
            );
        }
    }
}
