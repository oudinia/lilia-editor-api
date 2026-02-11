using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentSnapshotConfiguration : IEntityTypeConfiguration<DocumentSnapshot>
{
    public void Configure(EntityTypeBuilder<DocumentSnapshot> builder)
    {
        builder.ToTable("document_snapshots");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(s => s.CreatedBy).HasColumnName("created_by").HasMaxLength(255);
        builder.Property(s => s.Name).HasColumnName("name").HasMaxLength(255);
        builder.Property(s => s.BlocksSnapshot).HasColumnName("blocks_snapshot").HasColumnType("jsonb").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(s => s.DocumentId);

        builder.HasOne(s => s.Document)
            .WithMany(d => d.Snapshots)
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Creator)
            .WithMany()
            .HasForeignKey(s => s.CreatedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
