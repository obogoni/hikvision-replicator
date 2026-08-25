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
/// </summary>
public class UserRepository(AppDbContext dbContext)
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
    private static string? ConflictMessage(DbUpdateException exception) =>
        exception.InnerException
        is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } violation
            ? violation.ConstraintName switch
            {
                UserConfiguration.ExternalRefIndexName =>
                    IUserRepository.ExternalRefAlreadyRegistered,
                UserConfiguration.AccessCodeIndexName => IUserRepository.AccessCodeAlreadyInUse,
                _ => null,
            }
            : null;
}
