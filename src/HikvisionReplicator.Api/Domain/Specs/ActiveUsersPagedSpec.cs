using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// One page of the active catalogue, deleted spectators excluded (USR-45).
/// <para>
/// Ordered by <see cref="User.Id"/>: it is the primary key, so it is unique and therefore a
/// <b>total</b> order, and it is monotonic, so a spectator registered mid-listing is appended
/// rather than inserted between pages. That is what USR-44 needs — an order with ties admits two
/// valid orderings, and a page boundary falling inside a tie skips or repeats a user.
/// </para>
/// <para>
/// It does not include <see cref="User.Picture"/>, and this is the path where that matters most:
/// auto-including the bytes would put 40-200 KB on every row of every page (A-1, OD-4).
/// </para>
/// </summary>
public sealed class ActiveUsersPagedSpec : Specification<User>
{
    public ActiveUsersPagedSpec(int skip, int take)
    {
        Query.Where(user => user.DeletedAt == null).OrderBy(user => user.Id).Skip(skip).Take(take);
    }
}
