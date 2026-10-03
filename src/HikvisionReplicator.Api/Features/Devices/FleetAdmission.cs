using HikvisionReplicator.Api.Domain.Specs;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Features.Devices;

/// <summary>
/// Whether a reader may join the fleet at all (AD-021). The guard is fleet-admission, never
/// per-replication: a single enrolment is never blocked by it, because a turnstile refusing
/// one spectator is the failure this whole service exists to prevent.
/// <para>
/// The basis is the <em>active</em> roster. AD-015 sends every active spectator to every
/// reader, so what a reader will eventually have to hold is exactly that count; a tombstoned
/// spectator occupies nothing on the hardware (AD-034).
/// </para>
/// <para>
/// It lives here, beside the two slices that apply it, because REP-23 requires an update
/// lowering capacity to be refused on <em>the same terms</em> as REP-21 — and "the same
/// terms" is a promise about one sentence an operator reads, not two that happen to agree.
/// </para>
/// </summary>
public static class FleetAdmission
{
    /// <summary>
    /// The refusal a reader of this capacity earns, or <c>null</c> when it can hold the
    /// roster. Equal capacity passes: a reader holding exactly the crowd holds the crowd.
    /// </summary>
    public static async Task<ConflictError?> RefuseIfTooSmallAsync(
        IUserRepository users,
        int faceCapacity,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(users);

        var roster = await users.CountAsync(new ActiveUserCountSpec(), cancellationToken);

        return faceCapacity < roster
            ? new ConflictError(
                $"This device holds {faceCapacity} faces, but {roster} users are active."
            )
            : null;
    }
}
