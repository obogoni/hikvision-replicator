using System.Runtime.CompilerServices;
using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace HikvisionReplicator.IntegrationTests;

/// <summary>
/// Lets a worker test act at an exact point in a replication's life: just before a save that
/// moves it reaches the database, or just after that save commits. For the races the worker
/// must survive that no request can land on demand — a newer intent arriving between the
/// recorder's look and its save, a spectator removed between a claim and the call — which a
/// test would otherwise reach only by winning a scheduling coin-flip (AD-036).
/// <para>
/// It arranges; it never asserts. Every save in the host passes through it, so each script
/// picks out the move it is waiting for and does its work once.
/// </para>
/// </summary>
public sealed class ReplicationSaveHook : SaveChangesInterceptor
{
    /// <summary>One replication changing status in the save being made.</summary>
    public sealed record Move(
        int Id,
        int UserId,
        int DeviceId,
        ReplicationStatus From,
        ReplicationStatus To
    );

    private readonly Lock _gate = new();
    private readonly ConditionalWeakTable<DbContext, List<Move>> _saving = [];
    private Func<Move, Task>? _beforeCommit;
    private Func<Move, Task>? _afterCommit;

    /// <summary>Runs <paramref name="act"/> for each move, before its save is sent.</summary>
    public void BeforeCommit(Func<Move, Task> act)
    {
        lock (_gate)
            _beforeCommit = act;
    }

    /// <summary>Runs <paramref name="act"/> for each move, once its save has committed.</summary>
    public void AfterCommit(Func<Move, Task> act)
    {
        lock (_gate)
            _afterCommit = act;
    }

    public void Reset()
    {
        lock (_gate)
            (_beforeCommit, _afterCommit) = (null, null);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not { } context)
            return result;

        var moves = context
            .ChangeTracker.Entries<Replication>()
            .Where(entry => entry.State == EntityState.Modified)
            .Select(entry => new Move(
                entry.Entity.Id,
                entry.Entity.UserId,
                entry.Entity.DeviceId,
                entry.Property(replication => replication.Status).OriginalValue,
                entry.Entity.Status
            ))
            .Where(move => move.From != move.To)
            .ToList();
        _saving.AddOrUpdate(context, moves);

        Func<Move, Task>? act;
        lock (_gate)
            act = _beforeCommit;

        if (act is not null)
        {
            foreach (var move in moves)
                await act(move);
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(eventData);
        if (eventData.Context is not { } context || !_saving.TryGetValue(context, out var moves))
            return result;

        _saving.Remove(context);

        Func<Move, Task>? act;
        lock (_gate)
            act = _afterCommit;

        if (act is not null)
        {
            foreach (var move in moves)
                await act(move);
        }

        return result;
    }
}
