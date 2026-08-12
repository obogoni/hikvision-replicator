using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace HikvisionReplicator.Api.Infrastructure;

public class DeviceConfiguration : IEntityTypeConfiguration<Device>
{
    /// <summary>
    /// The unique index that makes the device address the authority for DEV-05/DEV-06.
    /// Named explicitly so the repository can recognise the constraint violation it
    /// raises and translate it into a ConflictError (AD-022).
    /// </summary>
    public const string AddressIndexName = "IX_devices_IpAddress_HttpPort";

    public const string TableName = "devices";

    public void Configure(EntityTypeBuilder<Device> entity)
    {
        entity.ToTable(TableName);

        entity.HasKey(d => d.Id);
        entity.Property(d => d.Id).ValueGeneratedOnAdd();
        entity.Property(d => d.Name).IsRequired().HasMaxLength(Device.MaxNameLength);
        entity.Property(d => d.Username).IsRequired().HasMaxLength(Device.MaxUsernameLength);
        entity.Property(d => d.EncryptedPassword).IsRequired();
        entity.Property(d => d.CreatedAt).IsRequired();
        entity.Property(d => d.UpdatedAt).IsRequired();

        entity
            .Property(d => d.IpAddress)
            .IsRequired()
            .HasConversion(
                new ValueConverter<IpAddress, string>(
                    ipAddress => ipAddress.Value,
                    value => IpAddress.FromPersistence(value)
                )
            );

        entity
            .Property(d => d.HttpPort)
            .IsRequired()
            .HasConversion(
                new ValueConverter<Port, int>(
                    httpPort => httpPort.Value,
                    value => Port.FromPersistence(value)
                )
            );

        entity
            .Property(d => d.FaceCapacity)
            .IsRequired()
            .HasConversion(
                new ValueConverter<FaceCapacity, int>(
                    faceCapacity => faceCapacity.Value,
                    value => FaceCapacity.FromPersistence(value)
                )
            );

        entity
            .HasIndex(d => new { d.IpAddress, d.HttpPort })
            .IsUnique()
            .HasDatabaseName(AddressIndexName);
    }
}
