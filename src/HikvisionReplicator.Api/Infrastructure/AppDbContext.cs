using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace HikvisionReplicator.Api.Infrastructure;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Device> Devices => Set<Device>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(entity =>
        {
            entity.HasKey(d => d.Id);
            entity.Property(d => d.Id).ValueGeneratedOnAdd();
            entity.Property(d => d.Name).IsRequired().HasMaxLength(100);
            entity.Property(d => d.IpAddress).IsRequired();
            entity.Property(d => d.HttpPort).IsRequired();
            entity.Property(d => d.Username).IsRequired().HasMaxLength(100);
            entity.Property(d => d.EncryptedPassword).IsRequired();
            entity.Property(d => d.CreatedAt).IsRequired();
            entity.Property(d => d.UpdatedAt).IsRequired();

            entity.HasIndex(d => new { d.IpAddress, d.HttpPort }).IsUnique();
        });
    }
}
