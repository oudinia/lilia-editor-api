using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class BlockPreviewConfiguration : IEntityTypeConfiguration<BlockPreview>
{
    public void Configure(EntityTypeBuilder<BlockPreview> builder)
    {
        builder.ToTable("block_previews");

        builder.HasKey(bp => bp.Id);
        builder.Property(bp => bp.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(bp => bp.BlockId).HasColumnName("block_id").IsRequired();
        builder.Property(bp => bp.Format).HasColumnName("format").HasMaxLength(20).IsRequired();
        builder.Property(bp => bp.Data).HasColumnName("data");
        builder.Property(bp => bp.RenderedAt).HasColumnName("rendered_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(bp => bp.BlockId);
        builder.HasIndex(bp => new { bp.BlockId, bp.Format }).IsUnique();

        builder.HasOne(bp => bp.Block)
            .WithMany()
            .HasForeignKey(bp => bp.BlockId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
