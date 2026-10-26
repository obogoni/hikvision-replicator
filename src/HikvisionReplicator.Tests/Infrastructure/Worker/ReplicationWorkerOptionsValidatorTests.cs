using HikvisionReplicator.Api.Infrastructure.Worker;
using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Tests.Infrastructure.Worker;

/// <summary>
/// WRK-30: a worker setting it cannot run on is refused, and the refusal names the setting. The
/// setting paths are written out literally — comparing against the validator's own text would
/// move with it (AD-036's tautology trap).
/// </summary>
public class ReplicationWorkerOptionsValidatorTests
{
    private static ValidateOptionsResult Validate(Action<ReplicationWorkerOptions> configure)
    {
        var options = new ReplicationWorkerOptions();
        configure(options);
        return new ReplicationWorkerOptionsValidator().Validate(null, options);
    }

    private static void Set(ReplicationWorkerOptions options, string setting, TimeSpan value)
    {
        switch (setting)
        {
            case "PollInterval":
                options.PollInterval = value;
                break;
            case "DeviceCallTimeout":
                options.DeviceCallTimeout = value;
                break;
            case "Lease":
                options.Lease = value;
                break;
            case "RetryBaseDelay":
                options.RetryBaseDelay = value;
                break;
            case "RetryMaxDelay":
                options.RetryMaxDelay = value;
                break;
            case "ProbeInitialDelay":
                options.ProbeInitialDelay = value;
                break;
            case "ProbeMaxDelay":
                options.ProbeMaxDelay = value;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setting));
        }
    }

    // ─── The spec's defaults, and that they are a configuration the worker can run on ───

    [Fact]
    public void Defaults_are_the_figures_the_spec_assumes()
    {
        var options = new ReplicationWorkerOptions();

        Assert.True(options.Enabled);
        Assert.Equal(TimeSpan.FromSeconds(1), options.PollInterval);
        Assert.Equal(TimeSpan.FromSeconds(10), options.DeviceCallTimeout);
        Assert.Equal(TimeSpan.FromSeconds(120), options.Lease);
        Assert.Equal(5, options.MaxAttempts);
        Assert.Equal(TimeSpan.FromSeconds(2), options.RetryBaseDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), options.RetryMaxDelay);
        Assert.Equal(2, options.PauseAfterConsecutiveUnreachable);
        Assert.Equal(TimeSpan.FromSeconds(5), options.ProbeInitialDelay);
        Assert.Equal(TimeSpan.FromSeconds(60), options.ProbeMaxDelay);
    }

    [Fact]
    public void Defaults_are_accepted()
    {
        var result = Validate(_ => { });

        Assert.True(result.Succeeded);
    }

    // ─── WRK-30: every interval, timeout and lease must be positive ───

    [Theory]
    [InlineData("PollInterval", 0)]
    [InlineData("PollInterval", -1)]
    [InlineData("DeviceCallTimeout", 0)]
    [InlineData("DeviceCallTimeout", -1)]
    [InlineData("Lease", 0)]
    [InlineData("Lease", -1)]
    [InlineData("RetryBaseDelay", 0)]
    [InlineData("RetryBaseDelay", -1)]
    [InlineData("RetryMaxDelay", 0)]
    [InlineData("RetryMaxDelay", -1)]
    [InlineData("ProbeInitialDelay", 0)]
    [InlineData("ProbeInitialDelay", -1)]
    [InlineData("ProbeMaxDelay", 0)]
    [InlineData("ProbeMaxDelay", -1)]
    public void A_span_that_is_not_positive_is_refused_by_name(string setting, int seconds)
    {
        var result = Validate(options => Set(options, setting, TimeSpan.FromSeconds(seconds)));

        Assert.True(result.Failed);
        Assert.Contains($"ReplicationWorker:{setting} must be positive", result.FailureMessage);
    }

    // ─── WRK-30: a maximum attempt count below 1, and the pause threshold alike ───

    [Fact]
    public void A_maximum_attempt_count_below_one_is_refused_by_name()
    {
        var result = Validate(options => options.MaxAttempts = 0);

        Assert.True(result.Failed);
        Assert.Contains("ReplicationWorker:MaxAttempts", result.FailureMessage);
    }

    [Fact]
    public void A_maximum_attempt_count_of_one_is_accepted()
    {
        Assert.True(Validate(options => options.MaxAttempts = 1).Succeeded);
    }

    [Fact]
    public void A_pause_threshold_below_one_is_refused_by_name()
    {
        var result = Validate(options => options.PauseAfterConsecutiveUnreachable = 0);

        Assert.True(result.Failed);
        Assert.Contains("ReplicationWorker:PauseAfterConsecutiveUnreachable", result.FailureMessage);
    }

    [Fact]
    public void A_pause_threshold_of_one_is_accepted()
    {
        Assert.True(Validate(options => options.PauseAfterConsecutiveUnreachable = 1).Succeeded);
    }

    // ─── WRK-30: a lease not greater than the call timeout would reclaim a live call ───

    [Theory]
    [InlineData(10)]
    [InlineData(5)]
    public void A_lease_not_greater_than_the_call_timeout_is_refused_by_name(int leaseSeconds)
    {
        var result = Validate(options =>
        {
            options.DeviceCallTimeout = TimeSpan.FromSeconds(10);
            options.Lease = TimeSpan.FromSeconds(leaseSeconds);
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "ReplicationWorker:Lease must be greater than ReplicationWorker:DeviceCallTimeout",
            result.FailureMessage
        );
    }

    [Fact]
    public void A_lease_just_greater_than_the_call_timeout_is_accepted()
    {
        var result = Validate(options =>
        {
            options.DeviceCallTimeout = TimeSpan.FromSeconds(10);
            options.Lease = TimeSpan.FromSeconds(10).Add(TimeSpan.FromTicks(1));
        });

        Assert.True(result.Succeeded);
    }

    // ─── WRK-30: a backoff whose ceiling is below its starting wait cannot double toward it ───

    [Fact]
    public void A_retry_ceiling_below_the_first_retry_wait_is_refused_by_name()
    {
        var result = Validate(options =>
        {
            options.RetryBaseDelay = TimeSpan.FromSeconds(10);
            options.RetryMaxDelay = TimeSpan.FromSeconds(9);
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "ReplicationWorker:RetryMaxDelay must not be less than ReplicationWorker:RetryBaseDelay",
            result.FailureMessage
        );
    }

    [Fact]
    public void A_retry_ceiling_equal_to_the_first_retry_wait_is_accepted()
    {
        var result = Validate(options =>
        {
            options.RetryBaseDelay = TimeSpan.FromSeconds(10);
            options.RetryMaxDelay = TimeSpan.FromSeconds(10);
        });

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void A_probe_ceiling_below_the_first_probe_wait_is_refused_by_name()
    {
        var result = Validate(options =>
        {
            options.ProbeInitialDelay = TimeSpan.FromSeconds(10);
            options.ProbeMaxDelay = TimeSpan.FromSeconds(9);
        });

        Assert.True(result.Failed);
        Assert.Contains(
            "ReplicationWorker:ProbeMaxDelay must not be less than ReplicationWorker:ProbeInitialDelay",
            result.FailureMessage
        );
    }

    [Fact]
    public void A_probe_ceiling_equal_to_the_first_probe_wait_is_accepted()
    {
        var result = Validate(options =>
        {
            options.ProbeInitialDelay = TimeSpan.FromSeconds(10);
            options.ProbeMaxDelay = TimeSpan.FromSeconds(10);
        });

        Assert.True(result.Succeeded);
    }
}
