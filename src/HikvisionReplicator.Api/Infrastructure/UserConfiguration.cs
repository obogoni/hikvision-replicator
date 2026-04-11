using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HikvisionReplicator.Api.Infrastructure;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> entity)
    {
        entity.HasKey(u => u.Id);
        entity.Property(u => u.Id).ValueGeneratedOnAdd();
        entity.Property(u => u.Name).IsRequired().HasMaxLength(100);
        entity.Property(u => u.CreatedAt).IsRequired();
        entity.Property(u => u.UpdatedAt).IsRequired();

        entity
            .Property(u => u.AccessCode)
            .IsRequired()
            .HasConversion(
                new ValueConverter<AccessCode, string>(
                    ac => ac.Value,
                    str => AccessCode.FromPersistence(str)
                )
            );

        entity.Property(u => u.FacePic);
    }
}
