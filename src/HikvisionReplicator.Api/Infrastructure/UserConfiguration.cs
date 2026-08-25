using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HikvisionReplicator.Api.Infrastructure;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    /// <summary>
    /// Unique across <b>every</b> row, tombstoned ones included. Resurrection (A-7) has to find a
    /// deleted user by its key and bring it back, so the key stays reserved after deletion: a
    /// second row must never be able to claim it. Named explicitly so the repository can
    /// recognise the violation it raises and translate it into a ConflictError (AD-022).
    /// </summary>
    public const string ExternalRefIndexName = "IX_users_ExternalRef";

    /// <summary>
    /// Unique only among users that are <b>not</b> deleted. USR-06 scopes access-code uniqueness
    /// to active users, so a tombstoned spectator's PIN returns to the pool while its
    /// <see cref="ExternalRefIndexName"/> entry does not. The asymmetry between the two indexes
    /// is deliberate — each one answers a different criterion, and swapping either breaks it.
    /// </summary>
    public const string AccessCodeIndexName = "IX_users_AccessCode";

    /// <summary>The partial-index predicate that scopes access-code uniqueness to active users.</summary>
    public const string ActiveRowsFilter = "\"DeletedAt\" IS NULL";

    public const string TableName = "users";

    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(user => user.Id);
        builder.Property(user => user.Id).ValueGeneratedOnAdd();
        builder.Property(user => user.Name).IsRequired().HasMaxLength(User.MaxNameLength);
        builder.Property(user => user.CreatedAt).IsRequired();
        builder.Property(user => user.UpdatedAt).IsRequired();

        // Null means active. Carries when the tombstone was set, and is the predicate the
        // access-code partial index filters on.
        builder.Property(user => user.DeletedAt);

        builder
            .Property(user => user.ExternalRef)
            .IsRequired()
            .HasMaxLength(ExternalRef.MaxLength)
            .HasConversion(
                new ValueConverter<ExternalRef, string>(
                    externalRef => externalRef.Value,
                    value => ExternalRef.FromPersistence(value)
                )
            );

        builder
            .Property(user => user.AccessCode)
            .IsRequired()
            .HasMaxLength(AccessCode.MaxLength)
            .HasConversion(
                new ValueConverter<AccessCode, string>(
                    accessCode => accessCode.Value,
                    value => AccessCode.FromPersistence(value)
                )
            );

        // The denormalized half of A-1: hash, byte size and dimensions live on the user row so
        // every read path — and Phase 2's change detection — answers without touching the bytes.
        builder.OwnsOne(
            user => user.Face,
            face =>
            {
                face.Property(fingerprint => fingerprint.ContentHash)
                    .HasColumnName("FaceContentHash")
                    .IsRequired();
                face.Property(fingerprint => fingerprint.ByteSize)
                    .HasColumnName("FaceByteSize")
                    .IsRequired();
                face.Property(fingerprint => fingerprint.Width)
                    .HasColumnName("FaceWidth")
                    .IsRequired();
                face.Property(fingerprint => fingerprint.Height)
                    .HasColumnName("FaceHeight")
                    .IsRequired();
            }
        );
        builder.Navigation(user => user.Face).IsRequired();

        builder
            .HasOne(user => user.Picture)
            .WithOne()
            .HasForeignKey<FacePicture>(picture => picture.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // NOT auto-included, and this is the whole point of splitting the bytes off (A-1, OD-4).
        // A face picture is 40-200 KB; auto-including it would put that on every row of every
        // catalogue query, which is the exact bloat the split exists to prevent. Phase 1 never
        // reads the bytes back at all — replication-worker opts in later with an explicit
        // specification. Adding .AutoInclude() here, or an .Include(u => u.Picture) to a list
        // specification, silently reintroduces the cost.
        builder.Navigation(user => user.Picture).AutoInclude(false);

        builder
            .HasIndex(user => user.ExternalRef)
            .IsUnique()
            .HasDatabaseName(ExternalRefIndexName);

        builder
            .HasIndex(user => user.AccessCode)
            .IsUnique()
            .HasDatabaseName(AccessCodeIndexName)
            .HasFilter(ActiveRowsFilter);
    }
}
