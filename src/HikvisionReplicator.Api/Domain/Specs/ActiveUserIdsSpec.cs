using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The id of every spectator a reader is owed — the roster a backfill expands into (REP-17).
/// <para>
/// Tombstoned spectators are excluded (REP-18): their face was destroyed at deletion, so
/// there is nothing to enrol. Ids only, and emphatically not aggregates — at 50,000
/// spectators the picture navigation alone would be gigabytes (A-1).
/// </para>
/// </summary>
public sealed class ActiveUserIdsSpec : Specification<User, int>
{
    public ActiveUserIdsSpec()
    {
        Query.Where(user => user.DeletedAt == null).Select(user => user.Id);
    }
}
