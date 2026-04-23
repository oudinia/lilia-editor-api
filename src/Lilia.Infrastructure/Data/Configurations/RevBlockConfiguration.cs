using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class RevBlockConfiguration : IEntityTypeConfiguration<RevBlock>
{
    public void Configure(EntityTypeBuilder<RevBlock> builder)
    {
        builder.ToTable("rev_blocks");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.RevDocumentId).HasColumnName("rev_document_id").IsRequired();
        builder.Property(b => b.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(b => b.Content).HasColumnName("content").HasColumnType("jsonb").IsRequired();
        builder.Property(b => b.SortOrder).HasColumnName("sort_order");
        builder.Property(b => b.ParentId).HasColumnName("parent_id");
        builder.Property(b => b.Depth).HasColumnName("depth");
        builder.Property(b => b.Path).HasColumnName("path").HasMaxLength(255);
        builder.Property(b => b.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("kept").IsRequired();
        builder.Property(b => b.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(b => b.Confidence).HasColumnName("confidence");
        builder.Property(b => b.Warnings).HasColumnName("warnings").HasColumnType("jsonb");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(b => b.RevDocumentId);
        builder.HasIndex(b => new { b.RevDocumentId, b.SortOrder });

        builder.HasOne(b => b.RevDocument)
            .WithMany(d => d.Blocks)
            .HasForeignKey(b => b.RevDocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<RevBlock>()
            .WithMany()
            .HasForeignKey(b => b.ParentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
