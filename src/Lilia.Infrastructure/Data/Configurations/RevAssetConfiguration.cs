using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class RevAssetConfiguration : IEntityTypeConfiguration<RevAsset>
{
    public void Configure(EntityTypeBuilder<RevAsset> builder)
    {
        builder.ToTable("rev_assets");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.RevDocumentId).HasColumnName("rev_document_id").IsRequired();
        builder.Property(a => a.FileName).HasColumnName("file_name").HasMaxLength(500).IsRequired();
        builder.Property(a => a.FileType).HasColumnName("file_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.FileSize).HasColumnName("file_size");
        builder.Property(a => a.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(a => a.Url).HasColumnName("url").HasMaxLength(1000);
        builder.Property(a => a.Width).HasColumnName("width");
        builder.Property(a => a.Height).HasColumnName("height");
        builder.Property(a => a.ContentHash).HasColumnName("content_hash").HasMaxLength(128);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(a => a.RevDocumentId);

        builder.HasOne(a => a.RevDocument)
            .WithMany()
            .HasForeignKey(a => a.RevDocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
