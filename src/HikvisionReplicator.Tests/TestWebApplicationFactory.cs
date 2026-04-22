using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.Tests;

public class TestWebApplicationFactory : WebApplicationFactory<Program>
{
    // Keep the connection open for the factory lifetime so the in-memory
    // database persists across all requests within a test.
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration(config =>
        {
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Encryption:Key"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
                }
            );
        });

        builder.ConfigureServices(services =>
        {
            // Remove the real DbContext registration
            var descriptor = services.SingleOrDefault(d =>
                d.ServiceType == typeof(DbContextOptions<AppDbContext>)
            );
            if (descriptor != null)
                services.Remove(descriptor);

            // All DbContext instances share the single open connection
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // Replace Hangfire job client with a no-op so jobs are never enqueued in tests.
            // Tests that verify job behaviour call UserSyncJob.Execute() directly.
            services.AddSingleton<IBackgroundJobClient, NoOpBackgroundJobClient>();

            // Create the schema once
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });

        builder.UseEnvironment("Test");
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
            _connection.Dispose();
    }

    private sealed class NoOpBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => "noop";
        public bool ChangeState(string jobId, IState state, string? fromState) => true;
    }
}
