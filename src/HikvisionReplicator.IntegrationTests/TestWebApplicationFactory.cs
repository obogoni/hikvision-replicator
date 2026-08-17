using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Boots the real application against the Testcontainers database. Overrides are added
/// as the last configuration source so they win over appsettings.json.
/// </summary>
public class TestWebApplicationFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>A valid Base64-encoded 32-byte AES key for tests.</summary>
    public const string TestEncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ConnectionStrings:DefaultConnection"] = connectionString,
                    ["Encryption:Key"] = TestEncryptionKey,
                }
            )
        );
    }
}
