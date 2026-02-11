using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ConversionAuditConfiguration : IEntityTypeConfiguration<ConversionAudit>
{
    public void Configure(EntityTypeBuilder<ConversionAudit> builder)
    {
        builder.ToTable("conversion_audits");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.JobId).HasColumnName("job_id");
        builder.Property(a => a.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(a => a.Action).HasColumnName("action").HasMaxLength(100).IsRequired();
        builder.Property(a => a.Details).HasColumnName("details").HasColumnType("jsonb");
        builder.Property(a => a.Timestamp).HasColumnName("timestamp").HasDefaultValueSql("NOW()");
        builder.Property(a => a.DurationMs).HasColumnName("duration_ms");

        builder.HasIndex(a => a.JobId);
        builder.HasIndex(a => a.UserId);
        builder.HasIndex(a => new { a.UserId, a.Timestamp });

        builder.HasOne(a => a.Job)
            .WithMany()
            .HasForeignKey(a => a.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.User)
            .WithMany()
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
