using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HikvisionReplicator.Api.Infrastructure;

public class ReplicationConfiguration : IEntityTypeConfiguration<Replication>
{
    public void Configure(EntityTypeBuilder<Replication> entity)
    {
        entity.HasKey(r => r.Id);
        entity.Property(r => r.Id).ValueGeneratedOnAdd();
        entity.Property(r => r.UserId).IsRequired();
        entity.Property(r => r.DeviceId).IsRequired();
        entity.Property(r => r.CreatedAt).IsRequired();
        entity.Property(r => r.UpdatedAt).IsRequired();
        entity.Property(r => r.Type).IsRequired().HasConversion<string>();
        entity.Property(r => r.Status).IsRequired().HasConversion<string>();
    }
}
