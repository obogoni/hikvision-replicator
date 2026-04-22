using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

public class ReplicationRepository(AppDbContext dbContext)
    : RepositoryBase<Replication>(dbContext), IRepository<Replication> { }
