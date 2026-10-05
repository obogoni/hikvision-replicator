using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OneOf;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The partial unique index is the authority for REP-09, and this is where its refusal stops
/// being a provider exception and becomes a domain answer (AD-022).
/// <para>
/// <b>The translation keys off the index <em>name</em>.</b> Renaming
/// <see cref="ReplicationConfiguration.PendingIndexName"/> without changing it here silently
/// degrades a 409 into a 500. A <c>23505</c> on any other index is deliberately not matched:
/// telling a caller its queued work collided, when what actually collided was an access code,
/// is a lie it cannot act on.
/// </para>
/// </summary>
public class ReplicationRepository(AppDbContext dbContext)
    : RepositoryBase<Replication>(dbContext),
        IReplicationRepository
{
    public OneOf<Success, ConflictError> TranslateIfDuplicatePending(DbUpdateException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception.InnerException
            is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ReplicationConfiguration.PendingIndexName,
        }
            ? new ConflictError(IReplicationRepository.DuplicatePendingWork)
            : new Success();
    }
}
