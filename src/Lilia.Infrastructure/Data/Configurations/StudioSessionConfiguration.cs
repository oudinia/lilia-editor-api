using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class StudioSessionConfiguration : IEntityTypeConfiguration<StudioSession>
{
    public void Configure(EntityTypeBuilder<StudioSession> builder)
    {
        builder.ToTable("studio_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.UserId).HasColumnName("user_id").HasMaxLength(100).IsRequired();
        builder.Property(s => s.DocumentId).HasColumnName("document_id").IsRequired();
        builder.Property(s => s.FocusedBlockId).HasColumnName("focused_block_id");
        builder.Property(s => s.Layout).HasColumnName("layout").HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
        builder.Property(s => s.CollapsedIds).HasColumnName("collapsed_ids");
        builder.Property(s => s.PinnedIds).HasColumnName("pinned_ids");
        builder.Property(s => s.ViewMode).HasColumnName("view_mode").HasMaxLength(20).HasDefaultValue("tree");
        builder.Property(s => s.LastAccessed).HasColumnName("last_accessed").HasDefaultValueSql("NOW()");

        builder.HasIndex(s => new { s.UserId, s.DocumentId }).IsUnique();

        builder.HasOne(s => s.Document)
            .WithMany()
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
