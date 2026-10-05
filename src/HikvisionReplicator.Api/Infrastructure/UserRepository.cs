using Ardalis.Specification.EntityFrameworkCore;
using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OneOf;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The database is the authority for both uniqueness rules (AD-022). A write that loses a race
/// past a pre-check comes back as PostgreSQL <c>23505</c> on one of the two named indexes and is
/// translated here into the <see cref="ConflictError"/> the pre-check would have produced.
/// <para>
/// <b>The translation keys off index <em>names</em>, and there are now two of them.</b> Renaming
/// either index in <see cref="UserConfiguration"/> without changing it here silently degrades a
/// 409 into a 500, which is why each constraint has an integration test that provokes the real
/// race. A <c>23505</c> on any other index is deliberately not matched: reporting an unrelated
/// collision as one of these two would be a lie the caller cannot act on.
/// </para>
/// <para>
/// <b>There is a third index in play, and it is not this repository's.</b> The fan-out stages
/// its rows into the very save below (AD-041), so the queue's pending index can lose a race
/// here too (REP-13). Rather than copy its name into a second switch — the exact fragility the
/// paragraph above describes — the queue is asked to translate what it owns.
/// </para>
/// </summary>
public class UserRepository(AppDbContext dbContext, IReplicationRepository replications)
    : RepositoryBase<User>(dbContext),
        IUserRepository
{
    public Task<OneOf<Success, ConflictError>> AddIfKeysFreeAsync(
        User user,
        CancellationToken cancellationToken
    )
    {
        dbContext.Users.Add(user);
        return SaveIfKeysFreeAsync(cancellationToken);
    }

    public async Task<OneOf<Success, ConflictError>> SaveIfKeysFreeAsync(
        CancellationToken cancellationToken
    )
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            return new Success();
        }
        catch (DbUpdateException exception) when (ConflictMessage(exception) is { } message)
        {
            return new ConflictError(message);
        }
    }

    public Task LoadPictureAsync(User user, CancellationToken cancellationToken) =>
        dbContext.Entry(user).Reference(tracked => tracked.Picture).LoadAsync(cancellationToken);

    /// <summary>
    /// The message for the key that actually collided, or <c>null</c> when the failure is
    /// something else — in which case the exception filter does not match and it propagates.
    /// </summary>
    private string? ConflictMessage(DbUpdateException exception)
    {
        var userKey =
            exception.InnerException
            is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
                ? violation.ConstraintName switch
                {
                    UserConfiguration.ExternalRefIndexName =>
                        IUserRepository.ExternalRefAlreadyRegistered,
                    UserConfiguration.AccessCodeIndexName =>
                        IUserRepository.AccessCodeAlreadyInUse,
                    _ => null,
                }
                : null;

        if (userKey is not null)
            return userKey;

        // The queue's own answer, or nothing. Its Success arm means "not my index — let it
        // propagate", which is what keeps a genuine fault a 500 rather than a conflict the
        // caller can do nothing about.
        return replications.TranslateIfDuplicatePending(exception).TryPickT1(out var queued, out _)
            ? queued.Message
            : null;
    }
}
