using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// Which adapter answers for the readers. Explicit and required, because the only adapter this
/// feature ships is a fake that always answers: left as a silent default it would report every
/// spectator enrolled while no turnstile knows anyone (design § Risks).
/// </summary>
public sealed class DeviceClientOptions
{
    public const string SectionName = "DeviceClient";

    /// <summary>The in-memory reader fleet — Development and the test harness only.</summary>
    public const string FakeAdapter = "Fake";

    public string? Adapter { get; set; }
}

/// <summary>
/// Wired with ValidateOnStart() so a missing or unknown adapter stops the process naming the
/// setting, rather than surfacing as an unresolved service on the worker's first call.
/// </summary>
public sealed class DeviceClientOptionsValidator : IValidateOptions<DeviceClientOptions>
{
    public const string AdapterPath =
        DeviceClientOptions.SectionName + ":" + nameof(DeviceClientOptions.Adapter);

    public ValidateOptionsResult Validate(string? name, DeviceClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Adapter))
        {
            return ValidateOptionsResult.Fail(
                $"{AdapterPath} is required. The only adapter available is "
                    + $"'{DeviceClientOptions.FakeAdapter}', until a real one ships."
            );
        }

        return options.Adapter == DeviceClientOptions.FakeAdapter
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{AdapterPath} '{options.Adapter}' is not a known adapter. The only adapter "
                    + $"available is '{DeviceClientOptions.FakeAdapter}'."
            );
    }
}
