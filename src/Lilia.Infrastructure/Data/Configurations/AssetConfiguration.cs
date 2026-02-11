using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class AssetConfiguration : IEntityTypeConfiguration<Asset>
{
    public void Configure(EntityTypeBuilder<Asset> builder)
    {
        builder.ToTable("assets");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(a => a.FileName).HasColumnName("file_name").HasMaxLength(255).IsRequired();
        builder.Property(a => a.FileType).HasColumnName("file_type").HasMaxLength(100).IsRequired();
        builder.Property(a => a.FileSize).HasColumnName("file_size").IsRequired();
        builder.Property(a => a.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(a => a.Url).HasColumnName("url");
        builder.Property(a => a.Width).HasColumnName("width");
        builder.Property(a => a.Height).HasColumnName("height");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(a => a.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(a => a.S3Bucket).HasColumnName("s3_bucket").HasMaxLength(255);
        builder.Property(a => a.ContentHash).HasColumnName("content_hash").HasMaxLength(255);
        builder.Property(a => a.UsageCount).HasColumnName("usage_count").HasDefaultValue(1);
        builder.Property(a => a.LastAccessedAt).HasColumnName("last_accessed_at");

        builder.HasIndex(a => a.DocumentId);
        builder.HasIndex(a => a.ContentHash);
        builder.HasIndex(a => a.UserId);

        builder.HasOne(a => a.Document)
            .WithMany(d => d.Assets)
            .HasForeignKey(a => a.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
