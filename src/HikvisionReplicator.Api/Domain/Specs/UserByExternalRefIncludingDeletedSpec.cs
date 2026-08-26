using Ardalis.Specification;

namespace HikvisionReplicator.Api.Domain.Specs;

/// <summary>
/// The spectator holding an external reference, <b>tombstoned or not</b>. The upsert path uses
/// this one, because the external reference stays reserved after deletion and a PUT naming a
/// tombstoned reference must resurrect that user rather than fail (A-7, USR-34).
/// <para>
/// <b>Not interchangeable with <see cref="UserByExternalRefSpec"/>.</b> Using this specification
/// on a read path would resurrect a deleted spectator into a GET response, contradicting USR-31's
/// promise that a deleted user is invisible to every read.
/// </para>
/// <para>
/// Like every specification in this feature it does not include <see cref="User.Picture"/>: a
/// resurrection replaces the picture outright, so loading the destroyed one would be pointless
/// as well as expensive (A-1).
/// </para>
/// </summary>
public sealed class UserByExternalRefIncludingDeletedSpec : Specification<User>
{
    public UserByExternalRefIncludingDeletedSpec(ExternalRef externalRef)
    {
        Query.Where(user => user.ExternalRef == externalRef);
    }
}
