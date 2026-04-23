using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportArchiveStatsConfiguration : IEntityTypeConfiguration<ImportArchiveStats>
{
    public void Configure(EntityTypeBuilder<ImportArchiveStats> builder)
    {
        builder.ToTable("import_archive_stats");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.InstanceId).HasColumnName("instance_id").IsRequired();
        builder.Property(s => s.DefinitionId).HasColumnName("definition_id");
        builder.Property(s => s.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(s => s.SourceFormat).HasColumnName("source_format").HasMaxLength(30);
        builder.Property(s => s.DocumentClass).HasColumnName("document_class").HasMaxLength(100);
        builder.Property(s => s.FinalState).HasColumnName("final_state").HasMaxLength(30).IsRequired();
        builder.Property(s => s.TotalBlocks).HasColumnName("total_blocks");
        builder.Property(s => s.BlockCountsByType).HasColumnName("block_counts_by_type").HasColumnType("jsonb");
        builder.Property(s => s.ErrorCount).HasColumnName("error_count");
        builder.Property(s => s.WarningCount).HasColumnName("warning_count");
        builder.Property(s => s.QualityScore).HasColumnName("quality_score");
        builder.Property(s => s.CoverageMappedPercent).HasColumnName("coverage_mapped_percent");
        builder.Property(s => s.UnsupportedTokenCount).HasColumnName("unsupported_token_count");
        builder.Property(s => s.InstanceCreatedAt).HasColumnName("instance_created_at");
        builder.Property(s => s.InstanceLastActivityAt).HasColumnName("instance_last_activity_at");
        builder.Property(s => s.ArchivedAt).HasColumnName("archived_at").HasDefaultValueSql("NOW()");
        builder.Property(s => s.LifetimeMinutes).HasColumnName("lifetime_minutes");

        // Indexed for analytics aggregation queries — "how many imports
        // of each format per month", "avg quality by document_class", etc.
        builder.HasIndex(s => s.OwnerId);
        builder.HasIndex(s => s.ArchivedAt);
        builder.HasIndex(s => s.FinalState);
        builder.HasIndex(s => s.SourceFormat);
    }
}
