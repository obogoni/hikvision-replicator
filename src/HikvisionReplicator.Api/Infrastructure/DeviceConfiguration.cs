using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HikvisionReplicator.Api.Infrastructure;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    public void Configure(EntityTypeBuilder<Device> entity)
    {
        entity.HasKey(d => d.Id);
        entity.Property(d => d.Id).ValueGeneratedOnAdd();
        entity.Property(d => d.Name).IsRequired().HasMaxLength(100);
        entity.Property(d => d.Username).IsRequired().HasMaxLength(100);
        entity.Property(d => d.EncryptedPassword).IsRequired();
        entity.Property(d => d.CreatedAt).IsRequired();
        entity.Property(d => d.UpdatedAt).IsRequired();

        entity.Property(d => d.IpAddress)
            .IsRequired()
            .HasConversion(
                new ValueConverter<IpAddress, string>(
                    ip => ip.Value,
                    str => IpAddress.FromPersistence(str)));

        entity.Property(d => d.HttpPort)
            .IsRequired()
            .HasConversion(
                new ValueConverter<Port, int>(
                    port => port.Value,
                    val => Port.FromPersistence(val)));

        entity.HasIndex(d => new { d.IpAddress, d.HttpPort }).IsUnique();
    }
}
