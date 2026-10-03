using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The id of every reader in the catalogue — the fan-out target list (REP-01).
/// <para>
/// It projects ids and nothing else. This read now happens on <em>every</em> user write, the
/// path AD-038 measures, so materialising twenty device aggregates to use twenty integers
/// would put the whole catalogue through the change tracker on the latency path AD-014 makes
/// primary.
/// </para>
/// </summary>
public sealed class RegisteredDeviceIdsSpec : Specification<Device, int>
{
    public RegisteredDeviceIdsSpec()
    {
        Query.Select(device => device.Id);
    }
}
