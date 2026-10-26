using HikvisionReplicator.Api.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// The application with its replication worker <b>running</b>, on a fake reader fleet and a
/// clock the test can hold still.
/// <para>
/// Everything else in the suite runs with the worker off, because its tests assert on rows a
/// running worker would drain. Intervals here are tens of milliseconds so a test observes the
/// worker in well under a second; the lease stays far above the call timeout, as the validator
/// demands and as WRK-22 needs so a slow call is never reclaimed mid-flight.
/// </para>
/// </summary>
public class WorkerWebApplicationFactory(
    string connectionString,
    IReadOnlyDictionary<string, string?>? settings = null
) : TestWebApplicationFactory(connectionString)
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>The database this host's worker drains.</summary>
    public string ConnectionString { get; } = connectionString;

    /// <summary>The clock the application reads; see <see cref="WorkerClock"/>.</summary>
    public WorkerClock Clock { get; } = new();

    /// <summary>Scripts run inside this host's own saves; see <see cref="ReplicationSaveHook"/>.</summary>
    public ReplicationSaveHook Saves { get; } = new();

    /// <summary>The reader fleet this host's worker delivers to.</summary>
    public FakeDeviceClient Readers => Services.GetRequiredService<FakeDeviceClient>();

    public AppDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(ConnectionString).Options);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // Added after the base factory's source, so these win over its Enabled=false.
        builder.ConfigureAppConfiguration(config =>
            config.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["ReplicationWorker:Enabled"] = "true",
                    ["ReplicationWorker:PollInterval"] = PollInterval.ToString(),
                    ["ReplicationWorker:DeviceCallTimeout"] = "00:00:05",
                    ["ReplicationWorker:Lease"] = "00:00:30",
                    ["ReplicationWorker:RetryBaseDelay"] = "00:00:00.100",
                    ["ReplicationWorker:RetryMaxDelay"] = "00:00:00.400",
                    ["ReplicationWorker:ProbeInitialDelay"] = "00:00:00.100",
                    ["ReplicationWorker:ProbeMaxDelay"] = "00:00:00.400",
                }
            )
        );

        // A test's own settings, last, so they win over the harness defaults above.
        if (settings is not null)
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(settings));

        builder.ConfigureServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.ConfigureDbContext<AppDbContext>(options => options.AddInterceptors(Saves));
        });
    }
}

/// <summary>
/// Real time by default, so every delay and timeout the worker waits on elapses normally; held
/// at an instant the test chose when a timestamp has to be asserted exactly (AD-023). Timers are
/// never frozen — only what <see cref="GetUtcNow"/> reports.
/// </summary>
public sealed class WorkerClock : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset? _frozenAt;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
            return _frozenAt ?? System.GetUtcNow();
    }

    /// <summary>Reports <paramref name="at"/> until told otherwise.</summary>
    public void Freeze(DateTimeOffset at)
    {
        lock (_gate)
            _frozenAt = at;
    }

    /// <summary>Back to real time.</summary>
    public void Thaw()
    {
        lock (_gate)
            _frozenAt = null;
    }
}
