using HikvisionReplicator.Api.Domain;
using HikvisionReplicator.Api.Shared;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The save is where an aggregate's events become somebody else's work (AD-042).
/// </summary>
public class AppDbContext : DbContext
{
    private readonly IDomainEventDispatcher? _dispatcher;

    /// <summary>
    /// Construction with no dispatch at all: <c>dotnet ef</c> builds the model with no
    /// container to resolve one from, and the integration harness drives the tables by hand
    /// to arrange state. Neither fans out, and neither should.
    /// <para>
    /// The application resolves the overload below instead — the container picks the
    /// constructor it can satisfy, and a missing registration would quietly fall back to this
    /// one. That is why the fan-out is asserted behaviourally rather than assumed: a handler
    /// made to throw must leave no row committed.
    /// </para>
    /// </summary>
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public AppDbContext(DbContextOptions<AppDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options) => _dispatcher = dispatcher;

    public DbSet<Device> Devices => Set<Device>();

    public DbSet<User> Users => Set<User>();

    public DbSet<Replication> Replications => Set<Replication>();

    public DbSet<BackfillIntent> BackfillIntents => Set<BackfillIntent>();

    /// <summary>
    /// Dispatch, then save, then forget — and the order is the whole design.
    /// <para>
    /// <b>Dispatch runs before <c>base</c></b> so that whatever a handler stages is part of
    /// this save rather than of a second one. An after-commit dispatch would leave the
    /// spectator committed and the work owed for them in a transaction that may never
    /// happen, which is REP-07 lost outright.
    /// </para>
    /// <para>
    /// <b>Events are cleared only after <c>base</c> returns.</b> Clearing them up front would
    /// mean a save that threw had silently thrown the events away too, and the retry would
    /// fan out nothing.
    /// </para>
    /// <para>
    /// Both overloads of <c>SaveChangesAsync</c> funnel through this one, so there is no way
    /// to save past the hook by picking the other signature.
    /// </para>
    /// </summary>
    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        // Materialised before the save: the change tracker is read again by base, and an
        // aggregate's events have to survive into the clearing pass below.
        var raisers = ChangeTracker
            .Entries<AggregateRoot>()
            .Select(entry => entry.Entity)
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        if (_dispatcher is not null && raisers.Count > 0)
        {
            await _dispatcher.DispatchAsync(
                [.. raisers.SelectMany(aggregate => aggregate.DomainEvents)],
                cancellationToken
            );
        }

        var written = await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);

        foreach (var aggregate in raisers)
            aggregate.ClearDomainEvents();

        return written;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
