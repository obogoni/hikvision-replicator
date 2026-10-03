using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The queue table. Its one real invariant is <see cref="PendingIndexName"/>: at most one
/// <c>Pending</c> row per (spectator, reader) pair, enforced by the database and never by a
/// read-then-write check (REP-09, AD-022).
/// </summary>
public class ReplicationConfiguration : IEntityTypeConfiguration<Replication>
{
    /// <summary>
    /// The partial unique index that <b>is</b> REP-09. It is scoped to <c>Pending</c> rows
    /// because the queue is an intent log: a pair accumulates any number of terminal rows —
    /// superseded, succeeded, failed — and only the one piece of outstanding work is unique.
    /// Named explicitly so <c>ReplicationRepository</c> can recognise the violation it raises
    /// and translate it into a ConflictError (AD-022).
    /// </summary>
    public const string PendingIndexName = "IX_replications_pending";

    /// <summary>
    /// The drain order feature 4 will read: lane first, because AD-038's whole preemption
    /// relationship is "Live before Bulk". Added now because it is free on an empty table and
    /// expensive once the queue is large.
    /// </summary>
    public const string LaneStatusIndexName = "IX_replications_lane_status";

    /// <summary>The partial-index predicate that scopes the pair's uniqueness to outstanding work.</summary>
    public const string PendingRowsFilter = "\"Status\" = 'Pending'";

    /// <summary>
    /// Enums are stored as words, not ordinals: feature 8 puts this table in front of
    /// operators, and inserting a member must not silently re-map the rows already written.
    /// </summary>
    public const int MaxEnumLength = 20;

    public const string TableName = "replications";

    public void Configure(EntityTypeBuilder<Replication> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(replication => replication.Id);
        builder.Property(replication => replication.Id).ValueGeneratedOnAdd();
        builder.Property(replication => replication.UserId).IsRequired();
        builder.Property(replication => replication.DeviceId).IsRequired();
        builder.Property(replication => replication.AttemptCount).IsRequired();
        builder.Property(replication => replication.CreatedAt).IsRequired();
        builder.Property(replication => replication.UpdatedAt).IsRequired();

        builder
            .Property(replication => replication.LastError)
            .HasMaxLength(Replication.MaxLastErrorLength);

        builder
            .Property(replication => replication.Operation)
            .IsRequired()
            .HasMaxLength(MaxEnumLength)
            .HasConversion<string>();

        builder
            .Property(replication => replication.Lane)
            .IsRequired()
            .HasMaxLength(MaxEnumLength)
            .HasConversion<string>();

        builder
            .Property(replication => replication.Status)
            .IsRequired()
            .HasMaxLength(MaxEnumLength)
            .HasConversion<string>();

        // Restrict, and the asymmetry with the device below is deliberate. A spectator is
        // tombstoned, never deleted (AD-034), so the row a replication points at always
        // survives — a cascade here would be dead code that silently swallowed a violation
        // if it ever did fire. The navigation is mapped so that work staged for a spectator
        // the database has not keyed yet gets its foreign key from EF's fix-up at save.
        builder
            .HasOne(replication => replication.User)
            .WithMany()
            .HasForeignKey(replication => replication.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Cascade, because DEV-25 is a hard delete: refusing to remove a reader that still
        // has work queued would regress a 204 into a constraint violation (REP-16, REP-40).
        // No navigation — a replication is only ever staged against a reader already in the
        // catalogue, so there is no unsaved-aggregate case to carry.
        builder
            .HasOne<Device>()
            .WithMany()
            .HasForeignKey(replication => replication.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(replication => replication.User).AutoInclude(false);

        builder
            .HasIndex(replication => new { replication.UserId, replication.DeviceId })
            .IsUnique()
            .HasDatabaseName(PendingIndexName)
            .HasFilter(PendingRowsFilter);

        builder
            .HasIndex(replication => new { replication.Lane, replication.Status })
            .HasDatabaseName(LaneStatusIndexName);
    }
}
