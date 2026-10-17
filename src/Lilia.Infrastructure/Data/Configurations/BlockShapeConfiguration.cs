using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class BlockShapeConfiguration : IEntityTypeConfiguration<BlockShape>
{
    public void Configure(EntityTypeBuilder<BlockShape> builder)
    {
        builder.ToTable("block_shapes");
        builder.HasKey(s => s.BlockId);
        builder.Property(s => s.BlockId).HasColumnName("block_id");
        builder.Property(s => s.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(s => s.Shape).HasColumnName("shape").HasMaxLength(200).IsRequired();
        builder.Property(s => s.Preview).HasColumnName("preview");
        builder.Property(s => s.Words).HasColumnName("words");
        builder.Property(s => s.Characters).HasColumnName("characters");
        builder.Property(s => s.ValidationStatus).HasColumnName("validation_status").HasMaxLength(20);
        builder.Property(s => s.ValidationKind).HasColumnName("validation_kind").HasMaxLength(200);
        builder.Property(s => s.Warnings).HasColumnName("warnings");
        builder.Property(s => s.ComputedAt).HasColumnName("computed_at").HasDefaultValueSql("NOW()");
        builder.HasIndex(s => s.DocumentId).HasDatabaseName("ix_block_shapes_document");
        // The shape goes with its block, whichever way the block is deleted (ExecuteDelete included).
        builder.HasOne<Block>().WithOne().HasForeignKey<BlockShape>(s => s.BlockId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class BlockShapeQueueItemConfiguration : IEntityTypeConfiguration<BlockShapeQueueItem>
{
    public void Configure(EntityTypeBuilder<BlockShapeQueueItem> builder)
    {
        builder.ToTable("block_shape_queue");
        builder.HasKey(q => q.BlockId);
        builder.Property(q => q.BlockId).HasColumnName("block_id");
        builder.Property(q => q.QueuedAt).HasColumnName("queued_at").HasDefaultValueSql("clock_timestamp()");
    }
}
