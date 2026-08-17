using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OneOf;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The database is the authority for address uniqueness (AD-022). A write that loses the
/// race past the pre-check comes back as PostgreSQL <c>23505</c> on the named address
/// index, and is translated here into the same <see cref="ConflictError"/> the pre-check
/// would have produced (DEV-06). Every other failure propagates untouched.
/// </summary>
public class DeviceRepository(AppDbContext dbContext)
    : RepositoryBase<Device>(dbContext),
        IDeviceRepository
{
    public Task<OneOf<Success, ConflictError>> AddIfAddressFreeAsync(
        Device device,
        CancellationToken cancellationToken
    )
    {
        dbContext.Devices.Add(device);
        return SaveIfAddressFreeAsync(cancellationToken);
    }

    public async Task<OneOf<Success, ConflictError>> SaveIfAddressFreeAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new Success();
        }
        catch (DbUpdateException exception) when (IsAddressConflict(exception))
        {
            return new ConflictError(IDeviceRepository.AddressAlreadyRegistered);
        }
    }

    private static bool IsAddressConflict(DbUpdateException exception) =>
        exception.InnerException
            is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: DeviceConfiguration.AddressIndexName,
        };
}
