using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportReviewSessionConfiguration : IEntityTypeConfiguration<ImportReviewSession>
{
    public void Configure(EntityTypeBuilder<ImportReviewSession> builder)
    {
        builder.ToTable("import_review_sessions");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.JobId).HasColumnName("job_id");
        builder.Property(s => s.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(s => s.DocumentTitle).HasColumnName("document_title").HasMaxLength(500).IsRequired();
        builder.Property(s => s.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("in_progress").IsRequired();
        builder.Property(s => s.OriginalWarnings).HasColumnName("original_warnings").HasColumnType("jsonb");
        builder.Property(s => s.DocumentId).HasColumnName("document_id");
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(s => s.ExpiresAt).HasColumnName("expires_at");

        builder.HasIndex(s => s.OwnerId);
        builder.HasIndex(s => s.JobId);

        builder.HasOne(s => s.Owner)
            .WithMany()
            .HasForeignKey(s => s.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(s => s.Job)
            .WithMany()
            .HasForeignKey(s => s.JobId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(s => s.Document)
            .WithMany()
            .HasForeignKey(s => s.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
