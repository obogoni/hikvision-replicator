using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// How many spectators a reader would eventually have to hold — the basis for the
/// fleet-admission guard (REP-21) and the capacity signal (REP-24).
/// <para>
/// Tombstoned spectators do not count: AD-015 sends every <em>active</em> user to every
/// device, so the eventual load is the active count and a deleted spectator occupies nothing.
/// It selects no columns at all, because it is consumed through a count — the one read cheaper
/// than projecting ids.
/// </para>
/// </summary>
public sealed class ActiveUserCountSpec : Specification<User>
{
    public ActiveUserCountSpec()
    {
        Query.Where(user => user.DeletedAt == null);
    }
}
