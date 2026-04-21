using Ardalis.Specification;
using HikvisionReplicator.Api.Domain;

namespace HikvisionReplicator.Api.Domain.Specs;

public class UserByExternalRefSpec : Specification<User>
{
    public UserByExternalRefSpec(string externalRef)
    {
        Query.Where(u => u.ExternalRef == externalRef);
    }
}
