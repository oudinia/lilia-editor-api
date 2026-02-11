using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("blocks");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(b => b.Type).HasColumnName("type").HasMaxLength(50).IsRequired();
        builder.Property(b => b.Content).HasColumnName("content").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(b => b.SortOrder).HasColumnName("sort_order").HasDefaultValue(0);
        builder.Property(b => b.ParentId).HasColumnName("parent_id");
        builder.Property(b => b.Depth).HasColumnName("depth").HasDefaultValue(0);
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.CreatedBy).HasColumnName("created_by").HasMaxLength(255);

        builder.HasIndex(b => b.DocumentId);
        builder.HasIndex(b => new { b.DocumentId, b.SortOrder });

        builder.HasOne(b => b.Document)
            .WithMany(d => d.Blocks)
            .HasForeignKey(b => b.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.Parent)
            .WithMany(b => b.Children)
            .HasForeignKey(b => b.ParentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(b => b.Creator)
            .WithMany()
            .HasForeignKey(b => b.CreatedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
