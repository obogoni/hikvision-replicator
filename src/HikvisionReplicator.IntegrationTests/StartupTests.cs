using System.Net;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Trace;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The behaviours that are decided while the application is starting, not while it is
/// handling a request: encryption-key validation (DEV-15), conditional tracing (DEV-16),
/// and the Development-only API documentation (DEV-17).
/// </summary>
[Collection(PostgresCollection.Name)]
public class StartupTests(PostgresFixture fixture)
{
    private const string OpenApiDocumentPath = "/openapi/v1.json";
    private const string ApiReferencePath = "/scalar/v1";
    private const string OtlpEndpointKey = "OpenTelemetry:OtlpEndpoint";

    /// <summary>A three-byte key — valid Base64, but not the 32 bytes AES-256 needs.</summary>
    private const string ThreeByteBase64Key = "AAAA";

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
}
