using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportBlockReviewConfiguration : IEntityTypeConfiguration<ImportBlockReview>
{
    public void Configure(EntityTypeBuilder<ImportBlockReview> builder)
    {
        builder.ToTable("import_block_reviews");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(r => r.BlockIndex).HasColumnName("block_index").IsRequired();
        builder.Property(r => r.BlockId).HasColumnName("block_id").HasMaxLength(255).IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending").IsRequired();
        builder.Property(r => r.ReviewedBy).HasColumnName("reviewed_by").HasMaxLength(255);
        builder.Property(r => r.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(r => r.OriginalContent).HasColumnName("original_content").HasColumnType("jsonb").IsRequired();
        builder.Property(r => r.OriginalType).HasColumnName("original_type").HasMaxLength(50).IsRequired();
        builder.Property(r => r.CurrentContent).HasColumnName("current_content").HasColumnType("jsonb");
        builder.Property(r => r.CurrentType).HasColumnName("current_type").HasMaxLength(50);
        builder.Property(r => r.Confidence).HasColumnName("confidence");
        builder.Property(r => r.Warnings).HasColumnName("warnings").HasColumnType("jsonb");
        builder.Property(r => r.SortOrder).HasColumnName("sort_order").IsRequired();
        builder.Property(r => r.Depth).HasColumnName("depth").IsRequired();

        builder.HasIndex(r => r.SessionId);
        builder.HasIndex(r => new { r.SessionId, r.BlockId });
        builder.HasIndex(r => new { r.SessionId, r.Status });

        builder.HasOne(r => r.Session)
            .WithMany(s => s.BlockReviews)
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(r => r.Reviewer)
            .WithMany()
            .HasForeignKey(r => r.ReviewedBy)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
