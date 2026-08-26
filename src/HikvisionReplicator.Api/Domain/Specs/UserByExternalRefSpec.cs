using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The <b>active</b> spectator holding an external reference. Every read path uses this one, so a
/// tombstoned user reports as not found (USR-31, USR-36).
/// <para>
/// <b>Not interchangeable with <see cref="UserByExternalRefIncludingDeletedSpec"/>.</b> Using this
/// specification on the upsert path would make a resurrection (A-7) look like an unregistered
/// reference: the tombstone is invisible here, so the upsert would try to insert a second row and
/// collide with the external-reference index instead of restoring the user.
/// </para>
/// <para>
/// It also deliberately does not include <see cref="User.Picture"/>. The bytes live in their own
/// table precisely so a lookup never pays for them (A-1); adding an Include here reintroduces
/// 40-200 KB per row on a path that has no use for it.
/// </para>
/// </summary>
public sealed class UserByExternalRefSpec : Specification<User>
{
    public UserByExternalRefSpec(ExternalRef externalRef)
    {
        Query.Where(user => user.ExternalRef == externalRef && user.DeletedAt == null);
    }
}
