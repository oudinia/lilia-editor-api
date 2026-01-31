using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentVersionConfiguration : IEntityTypeConfiguration<DocumentVersion>
{
    public void Configure(EntityTypeBuilder<DocumentVersion> builder)
    {
        builder.ToTable("document_versions");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(v => v.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(v => v.VersionNumber).HasColumnName("version_number").IsRequired();
        builder.Property(v => v.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(v => v.Snapshot).HasColumnName("snapshot").HasColumnType("jsonb").IsRequired();
        builder.Property(v => v.CreatedBy).HasColumnName("created_by").HasMaxLength(255);
        builder.Property(v => v.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(v => v.DocumentId);

        builder.HasOne(v => v.Document)
            .WithMany(d => d.Versions)
            .HasForeignKey(v => v.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(v => v.Creator)
            .WithMany()
            .HasForeignKey(v => v.CreatedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
