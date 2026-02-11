using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class JobConfiguration : IEntityTypeConfiguration<Job>
{
    public void Configure(EntityTypeBuilder<Job> builder)
    {
        builder.ToTable("jobs");

        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(j => j.UserId).HasColumnName("user_id").HasMaxLength(255).IsRequired();
        builder.Property(j => j.DocumentId).HasColumnName("document_id");
        builder.Property(j => j.JobType).HasColumnName("job_type").HasMaxLength(20).IsRequired();
        builder.Property(j => j.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        builder.Property(j => j.Progress).HasColumnName("progress").HasDefaultValue(0);
        builder.Property(j => j.SourceFormat).HasColumnName("source_format").HasMaxLength(20);
        builder.Property(j => j.TargetFormat).HasColumnName("target_format").HasMaxLength(20);
        builder.Property(j => j.SourceFileName).HasColumnName("source_file_name").HasMaxLength(500);
        builder.Property(j => j.ResultFileName).HasColumnName("result_file_name").HasMaxLength(500);
        builder.Property(j => j.ResultUrl).HasColumnName("result_url");
        builder.Property(j => j.ErrorMessage).HasColumnName("error_message");
        builder.Property(j => j.Direction).HasColumnName("direction").HasMaxLength(20);
        builder.Property(j => j.InputFileKey).HasColumnName("input_file_key").HasMaxLength(500);
        builder.Property(j => j.InputFileSize).HasColumnName("input_file_size");
        builder.Property(j => j.OutputFileKey).HasColumnName("output_file_key").HasMaxLength(500);
        builder.Property(j => j.Options).HasColumnName("options").HasColumnType("jsonb");
        builder.Property(j => j.ErrorDetails).HasColumnName("error_details").HasColumnType("jsonb");
        builder.Property(j => j.RetryCount).HasColumnName("retry_count").HasDefaultValue(0);
        builder.Property(j => j.MaxRetries).HasColumnName("max_retries").HasDefaultValue(3);
        builder.Property(j => j.StartedAt).HasColumnName("started_at");
        builder.Property(j => j.ExpiresAt).HasColumnName("expires_at");
        builder.Property(j => j.Metadata).HasColumnName("metadata").HasColumnType("jsonb");
        builder.Property(j => j.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(j => j.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(j => j.CompletedAt).HasColumnName("completed_at");

        builder.HasIndex(j => j.UserId);
        builder.HasIndex(j => j.Status);
        builder.HasIndex(j => j.JobType);
        builder.HasIndex(j => j.CreatedAt);

        builder.HasOne(j => j.User)
            .WithMany()
            .HasForeignKey(j => j.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(j => j.Document)
            .WithMany()
            .HasForeignKey(j => j.DocumentId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
