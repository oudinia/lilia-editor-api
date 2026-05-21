using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class SyncTelemetryEventConfiguration : IEntityTypeConfiguration<SyncTelemetryEvent>
{
    public void Configure(EntityTypeBuilder<SyncTelemetryEvent> builder)
    {
        builder.ToTable("sync_telemetry_events", t =>
        {
            // Closed vocabularies enforced in-database. New values
            // require a migration; keeps the analytics surface stable.
            t.HasCheckConstraint("ck_sync_telemetry_severity",
                "severity IN ('info','warn','error')");
            t.HasCheckConstraint("ck_sync_telemetry_source",
                "source IN ('server','client')");
            t.HasCheckConstraint("ck_sync_telemetry_event_kind",
                "event_kind IN ('conflict','sync_error','retry_exhausted','offline_span')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.EventKind).HasColumnName("event_kind").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(20).HasDefaultValue("warn").IsRequired();
        builder.Property(e => e.Source).HasColumnName("source").HasMaxLength(20).HasDefaultValue("server").IsRequired();
        builder.Property(e => e.DocumentId).HasColumnName("document_id");
        builder.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(e => e.AttemptCount).HasColumnName("attempt_count");
        builder.Property(e => e.DurationMs).HasColumnName("duration_ms");
        builder.Property(e => e.Detail).HasColumnName("detail").HasMaxLength(500);
        builder.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

        builder.HasIndex(e => new { e.EventKind, e.CreatedAt })
            .HasDatabaseName("ix_sync_telemetry_kind_recent")
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.DocumentId, e.CreatedAt })
            .HasDatabaseName("ix_sync_telemetry_document_recent")
            .IsDescending(false, true)
            .HasFilter("document_id IS NOT NULL");
    }
}
