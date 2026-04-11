using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

public class UserRepository(AppDbContext dbContext)
    : RepositoryBase<User>(dbContext),
        IRepository<User> { }
