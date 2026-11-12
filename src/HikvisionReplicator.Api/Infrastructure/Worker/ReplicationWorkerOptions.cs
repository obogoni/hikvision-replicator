using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure.Worker;

/// <summary>
/// Every interval and limit the worker runs on, as configuration rather than constants. The
/// defaults are the spec's: most are agent defaults awaiting confirmation against real readers
/// (spec § Assumptions), so correcting one must be a configuration change, not a code change.
/// </summary>
public sealed class ReplicationWorkerOptions
{
    public const string SectionName = "ReplicationWorker";

    /// <summary>
    /// Whether the worker runs in this process at all. Off in the shared test harness, whose
    /// tests assert on rows a running worker would drain.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>WRK-31: how long an idle loop waits before looking at the queue again.</summary>
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// WRK-11: a call still unanswered after this is treated as unreachable. It is also the
    /// longest a live item can wait behind one bulk call on the same reader.
    /// </summary>
    public TimeSpan DeviceCallTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// WRK-22: work in progress for longer than this is presumed abandoned by a crashed worker.
    /// Must exceed <see cref="DeviceCallTimeout"/>, or a live call would be reclaimed mid-flight.
    /// </summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(120);

    /// <summary>WRK-17: the rejection count at which a record is dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>WRK-16: the wait after the first rejection, doubling after each one.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>WRK-16: the ceiling on that doubling.</summary>
    public TimeSpan RetryMaxDelay { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>WRK-12: consecutive unreachable calls that pause a reader.</summary>
    public int PauseAfterConsecutiveUnreachable { get; set; } = 2;

    /// <summary>WRK-13: the wait before a paused reader's first probe, doubling after each failed one.</summary>
    public TimeSpan ProbeInitialDelay { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>WRK-13: the ceiling on that doubling.</summary>
    public TimeSpan ProbeMaxDelay { get; set; } = TimeSpan.FromSeconds(60);
}

/// <summary>
/// WRK-30. Wired with ValidateOnStart() so a setting the worker cannot run on aborts startup
/// with its name, rather than surfacing as a hot loop or a backlog that never drains — by which
/// point spectators are already at the turnstile. Every failing setting is reported, not only
/// the first.
/// </summary>
public sealed class ReplicationWorkerOptionsValidator : IValidateOptions<ReplicationWorkerOptions>
{
    private const string Section = ReplicationWorkerOptions.SectionName;

    public ValidateOptionsResult Validate(string? name, ReplicationWorkerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        RequirePositive(failures, nameof(options.PollInterval), options.PollInterval);
        RequirePositive(failures, nameof(options.DeviceCallTimeout), options.DeviceCallTimeout);
        RequirePositive(failures, nameof(options.Lease), options.Lease);
        RequirePositive(failures, nameof(options.RetryBaseDelay), options.RetryBaseDelay);
        RequirePositive(failures, nameof(options.RetryMaxDelay), options.RetryMaxDelay);
        RequirePositive(failures, nameof(options.ProbeInitialDelay), options.ProbeInitialDelay);
        RequirePositive(failures, nameof(options.ProbeMaxDelay), options.ProbeMaxDelay);

        if (options.MaxAttempts < 1)
            failures.Add($"{Section}:{nameof(options.MaxAttempts)} must be at least 1.");

        if (options.PauseAfterConsecutiveUnreachable < 1)
        {
            failures.Add(
                $"{Section}:{nameof(options.PauseAfterConsecutiveUnreachable)} must be at least 1."
            );
        }

        if (options.Lease <= options.DeviceCallTimeout)
        {
            failures.Add(
                $"{Section}:{nameof(options.Lease)} must be greater than "
                    + $"{Section}:{nameof(options.DeviceCallTimeout)}, or a call still in flight "
                    + "would be reclaimed."
            );
        }

        if (options.RetryMaxDelay < options.RetryBaseDelay)
        {
            failures.Add(
                $"{Section}:{nameof(options.RetryMaxDelay)} must not be less than "
                    + $"{Section}:{nameof(options.RetryBaseDelay)}."
            );
        }

        if (options.ProbeMaxDelay < options.ProbeInitialDelay)
        {
            failures.Add(
                $"{Section}:{nameof(options.ProbeMaxDelay)} must not be less than "
                    + $"{Section}:{nameof(options.ProbeInitialDelay)}."
            );
        }

        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }

    private static void RequirePositive(List<string> failures, string setting, TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
            failures.Add($"{Section}:{setting} must be positive.");
    }
}
