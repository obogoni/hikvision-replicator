using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using OneOf;

namespace HikvisionReplicator.Api.Shared;

/// <summary>
/// Reads the queue, and translates the one constraint violation that is a domain answer
/// rather than a fault (AD-022).
/// <para>
/// There is no save method here on purpose: the fan-out stages into the change tracker the
/// calling slice already owns and never saves (AD-041), so the duplicate-pending violation
/// surfaces from <em>that</em> slice's save. This interface gives it the translation, not
/// the write.
/// </para>
/// </summary>
public interface IReplicationRepository : IRepository<Replication>
{
    /// <summary>
    /// What a caller sees when two intents for the same spectator and reader reach the
    /// pending index at once (REP-13). It is a conflict, never a <c>500</c>: the integrator
    /// re-sends, exactly as it already does for the upsert races the registry ships.
    /// </summary>
    const string DuplicatePendingWork =
        "Replication work for this user and device is already queued.";

    /// <summary>
    /// Reports whether a failed save was the pending index refusing a duplicate.
    /// <para>
    /// The <see cref="ConflictError"/> arm means it was, and carries
    /// <see cref="DuplicatePendingWork"/>. The <see cref="Success"/> arm means it was
    /// <b>not</b> — a <c>23505</c> on another index, or any other failure — and the caller
    /// must let the exception propagate rather than report a collision the caller cannot act
    /// on.
    /// </para>
    /// </summary>
    OneOf<Success, ConflictError> TranslateIfDuplicatePending(DbUpdateException exception);

    /// <summary>
    /// Reports whether a failed save lost a row-version race on queued work and nothing else
    /// (AD-044) — typically the fan-out superseding a <c>Pending</c> row the worker claimed
    /// between the fan-out's read and its save. The caller answers it with
    /// <see cref="DuplicatePendingWork"/>: the integrator re-sends, exactly as REP-13 already
    /// behaves, and the re-send finds the claim and queues beside it (REP-12).
    /// <para>
    /// <b>Only when every conflicting entry is a replication.</b> A concurrency failure on a
    /// spectator or a reader — a row removed behind a tracked instance — is a fault, and a
    /// conflict the caller can do nothing about would hide it.
    /// </para>
    /// <para>
    /// Static rather than an instance member because it keys off the entries' type, not off
    /// anything a repository holds — so the device repository can ask it without taking the
    /// queue as a dependency.
    /// </para>
    /// </summary>
    static bool LostRaceForQueuedWork(DbUpdateException exception) =>
        exception is DbUpdateConcurrencyException { Entries.Count: > 0 } concurrency
        && concurrency.Entries.All(entry => entry.Entity is Replication);
}
