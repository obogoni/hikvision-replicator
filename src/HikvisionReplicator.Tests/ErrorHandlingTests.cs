using System.Net;
using System.Text.Json;
using HikvisionReplicator.Api.Features.Devices.ListDevices;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace HikvisionReplicator.Tests;

/// <summary>
/// Boots the application against a real PostgreSQL container so startup succeeds, then
/// stops the container — every request afterwards meets an unreachable database (DEV-14).
/// </summary>
public sealed class UnreachableDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .Build();

    private TestWebApplicationFactory _factory = null!;

    public HttpClient Client { get; private set; } = null!;

    /// <summary>The details a leaking response would expose.</summary>
    public NpgsqlConnectionStringBuilder Connection { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var connectionString = _container.GetConnectionString();
        Connection = new NpgsqlConnectionStringBuilder(connectionString);

        _factory = new TestWebApplicationFactory(connectionString);

        // Building the host applies the migrations while the database is still up.
        Client = _factory.CreateClient();

        await _container.StopAsync();
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        _factory?.Dispose();
        await _container.DisposeAsync();
    }
}

public class DatabaseUnreachableTests(UnreachableDatabaseFixture fixture)
    : IClassFixture<UnreachableDatabaseFixture>
{
    // ─── DEV-14: an unreachable database is a service outage ─────────────

    [Fact]
    public async Task Request_made_while_the_database_is_unreachable_reports_a_service_outage()
    {
        var response = await fixture.Client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(503, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            GlobalExceptionHandler.DatabaseUnavailableTitle,
            body.RootElement.GetProperty("title").GetString()
        );
        Assert.Equal(
            GlobalExceptionHandler.DatabaseUnavailableDetail,
            body.RootElement.GetProperty("detail").GetString()
        );
    }

    [Fact]
    public async Task Service_outage_response_describes_nothing_about_the_database_connection()
    {
        var response = await fixture.Client.GetAsync("/api/devices");

        var body = await response.Content.ReadAsStringAsync();

        string[] connectionDetails =
        [
            fixture.Connection.Host!,
            fixture.Connection.Port.ToString(),
            fixture.Connection.Database!,
            fixture.Connection.Username!,
            fixture.Connection.Password!,
        ];
        foreach (var detail in connectionDetails)
            Assert.DoesNotContain(detail, body, StringComparison.OrdinalIgnoreCase);

        string[] diagnosticLeaks =
        [
            "Npgsql",
            "Exception",
            "stacktrace",
            "   at ",
            "Host=",
            "Password=",
            "Connection refused",
        ];
        foreach (var leak in diagnosticLeaks)
            Assert.DoesNotContain(leak, body, StringComparison.OrdinalIgnoreCase);
    }
}

[Collection(PostgresCollection.Name)]
public class ErrorHandlingTests(PostgresFixture fixture)
{
    /// <summary>A failure that has nothing to do with the database.</summary>
    private sealed class FailingListDevicesService : IListDevicesService
    {
        public Task<IReadOnlyList<DeviceResponse>> ExecuteAsync(
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("Catalogue projection is broken.");
    }

    // ─── DEV-14: everything that is not a database failure stays a 500 ───

    [Fact]
    public async Task Unexpected_failure_reports_an_internal_error()
    {
        using var factory = fixture.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped<IListDevicesService, FailingListDevicesService>()
            )
        );
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/devices");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var raw = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(raw);

        Assert.Equal(500, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(
            GlobalExceptionHandler.UnexpectedErrorDetail,
            body.RootElement.GetProperty("detail").GetString()
        );
        Assert.DoesNotContain(
            "Catalogue projection is broken.",
            raw,
            StringComparison.OrdinalIgnoreCase
        );
    }
}
