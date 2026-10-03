using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The one row that records a newly registered reader is owed the whole active roster —
/// instead of the 50,000 rows the naive form would put inside an HTTP request (REP-14).
/// </summary>
public class BackfillIntentConfiguration : IEntityTypeConfiguration<BackfillIntent>
{
    /// <summary>
    /// One intent per reader, and the database is what says so (REP-14). Unlike the queue's
    /// pending index this one is unfiltered: an expanded intent still occupies the reader's
    /// slot, which is what stops a second expansion from ever being recorded.
    /// </summary>
    public const string DeviceIndexName = "IX_backfill_intents_device";

    public const string TableName = "backfill_intents";

    public void Configure(EntityTypeBuilder<BackfillIntent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(intent => intent.Id);
        builder.Property(intent => intent.Id).ValueGeneratedOnAdd();
        builder.Property(intent => intent.DeviceId).IsRequired();
        builder.Property(intent => intent.ExpandedAt);
        builder.Property(intent => intent.CreatedAt).IsRequired();
        builder.Property(intent => intent.UpdatedAt).IsRequired();

        builder
            .Property(intent => intent.Status)
            .IsRequired()
            .HasMaxLength(ReplicationConfiguration.MaxEnumLength)
            .HasConversion<string>();

        // Cascade for the same reason the queue does: DEV-25 hard-deletes a reader, and a
        // debt owed to a decommissioned one is owed to nobody (REP-16). The navigation is
        // mapped so an intent staged for a reader the database has not keyed yet gets its
        // foreign key from EF's fix-up at save.
        builder
            .HasOne(intent => intent.Device)
            .WithMany()
            .HasForeignKey(intent => intent.DeviceId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(intent => intent.Device).AutoInclude(false);

        builder
            .HasIndex(intent => intent.DeviceId)
            .IsUnique()
            .HasDatabaseName(DeviceIndexName);
    }
}
