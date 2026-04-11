using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;

namespace HikvisionReplicator.Api.Infrastructure;

public class DeviceRepository(AppDbContext dbContext)
    : RepositoryBase<Device>(dbContext), IRepository<Device>
{
}
