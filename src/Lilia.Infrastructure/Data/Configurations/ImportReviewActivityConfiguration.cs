using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportReviewActivityConfiguration : IEntityTypeConfiguration<ImportReviewActivity>
{
    public void Configure(EntityTypeBuilder<ImportReviewActivity> builder)
    {
        builder.ToTable("import_review_activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(a => a.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(a => a.BlockId).HasColumnName("block_id").HasMaxLength(255);
        builder.Property(a => a.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(a => a.SessionId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => new { a.SessionId, a.CreatedAt });

        builder.HasOne(a => a.Session)
            .WithMany(s => s.Activities)
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
