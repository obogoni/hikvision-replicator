using HikvisionReplicator.Api.Domain;
using OneOf;

namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Persists spectators with both uniqueness invariants enforced by the database (AD-022).
/// The constraint violations are translated into <see cref="ConflictError"/>s here, so no slice
/// ever sees a provider exception.
/// <para>
/// There are two of them and they are not interchangeable, so each collision keeps its own
/// message: an integrator that re-sent a key needs a different correction from one whose PIN
/// generator collided with another spectator's.
/// </para>
/// </summary>
public interface IUserRepository : IRepository<User>
{
    /// <summary>
    /// The external reference is unique across every row, tombstones included, so this is also
    /// what a caller sees when it loses a create race (USR-07).
    /// </summary>
    const string ExternalRefAlreadyRegistered =
        "A user is already registered under this external reference.";

    /// <summary>Access codes are unique among active users only (USR-06, USR-08).</summary>
    const string AccessCodeAlreadyInUse = "This access code is already in use by another user.";

    /// <summary>Inserts the spectator, or reports which key was already taken.</summary>
    Task<OneOf<Success, ConflictError>> AddIfKeysFreeAsync(
        User user,
        CancellationToken cancellationToken
    );

    /// <summary>Saves pending changes, or reports which key was already taken.</summary>
    Task<OneOf<Success, ConflictError>> SaveIfKeysFreeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Brings a spectator's stored face picture into the tracked graph.
    /// <para>
    /// No specification includes the navigation, because no read path has any use for 40-200 KB
    /// of JPEG (A-1). The two paths that must <em>change</em> the bytes — replacing them on an
    /// update and destroying them on a removal — need the row in the graph for the write to be
    /// part of the same transaction, so they ask for it here and pay for it explicitly.
    /// </para>
    /// </summary>
    Task LoadPictureAsync(User user, CancellationToken cancellationToken);
}
