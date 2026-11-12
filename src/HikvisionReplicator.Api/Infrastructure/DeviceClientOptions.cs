using Microsoft.Extensions.Options;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// Which adapter answers for the readers. Explicit and required, because one of the adapters is
/// a fake that always answers: left as a silent default it would report every spectator
/// enrolled while no turnstile knows anyone (design § Risks).
/// </summary>
public sealed class DeviceClientOptions
{
    public const string SectionName = "DeviceClient";

    /// <summary>The in-memory reader fleet — Development and the test harness only.</summary>
    public const string FakeAdapter = "Fake";

    /// <summary>The real readers, over Hikvision ISAPI (ISA-23).</summary>
    public const string IsapiAdapter = "Isapi";

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

    /// <summary>Both values an operator may choose from, as every failure names them (ISA-23).</summary>
    private const string KnownAdapters =
        $"'{DeviceClientOptions.FakeAdapter}' or '{DeviceClientOptions.IsapiAdapter}'";

    public ValidateOptionsResult Validate(string? name, DeviceClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Adapter))
            return ValidateOptionsResult.Fail($"{AdapterPath} is required: {KnownAdapters}.");

        return options.Adapter is DeviceClientOptions.FakeAdapter or DeviceClientOptions.IsapiAdapter
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{AdapterPath} '{options.Adapter}' is not a known adapter: {KnownAdapters}."
            );
    }
}
