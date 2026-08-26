using HikvisionReplicator.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HikvisionReplicator.Api.Infrastructure;

/// <summary>
/// The canonical JPEG bytes, in their own table so the catalogue can be queried without them
/// (A-1). Cascade delete, so removing a user's row can never leave the bytes behind; the
/// relationship is required, so severing it destroys the picture (A-5, USR-30).
/// </summary>
public class FacePictureConfiguration : IEntityTypeConfiguration<FacePicture>
{
    public const string TableName = "face_pictures";

    public void Configure(EntityTypeBuilder<FacePicture> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName);

        builder.HasKey(picture => picture.Id);
        builder.Property(picture => picture.Id).ValueGeneratedOnAdd();
        builder.Property(picture => picture.UserId).IsRequired();
        builder.Property(picture => picture.Content).IsRequired();
    }
}
