using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportTelemetryEventConfiguration : IEntityTypeConfiguration<ImportTelemetryEvent>
{
    public void Configure(EntityTypeBuilder<ImportTelemetryEvent> builder)
    {
        builder.ToTable("import_telemetry_events", t =>
        {
            // Closed vocabularies enforced in-database. New values
            // require a migration; keeps the analytics surface stable.
            t.HasCheckConstraint("ck_telemetry_severity",
                "severity IN ('info','warn','error')");
            t.HasCheckConstraint("ck_telemetry_source_format",
                "source_format IN ('latex','docx','epub','pdf','lml','overleaf-zip')");
            t.HasCheckConstraint("ck_telemetry_event_kind",
                "event_kind IN (" +
                "'unknown_env','unhandled_token','silent_fallback'," +
                "'cell_cleanup_applied','partial_parse','expected_leak_hit'," +
                "'cmd_passthrough','unsupported_block_emitted','parser_warning')");
        });

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(e => e.EventKind).HasColumnName("event_kind").HasMaxLength(40).IsRequired();
        builder.Property(e => e.Severity).HasColumnName("severity").HasMaxLength(20).HasDefaultValue("warn").IsRequired();
        builder.Property(e => e.SourceFormat).HasColumnName("source_format").HasMaxLength(20).IsRequired();
        builder.Property(e => e.TokenOrEnv).HasColumnName("token_or_env").HasMaxLength(120);
        builder.Property(e => e.BlockKindEmitted).HasColumnName("block_kind_emitted").HasMaxLength(40);
        builder.Property(e => e.BlockKindExpected).HasColumnName("block_kind_expected").HasMaxLength(40);
        builder.Property(e => e.ImportSessionId).HasColumnName("import_session_id");
        builder.Property(e => e.DocumentId).HasColumnName("document_id");
        builder.Property(e => e.UserId).HasColumnName("user_id").HasMaxLength(255);
        builder.Property(e => e.SampleText).HasColumnName("sample_text").HasMaxLength(200);
        builder.Property(e => e.SourceFileName).HasColumnName("source_file_name").HasMaxLength(500);
        builder.Property(e => e.Metadata).HasColumnName("metadata").HasColumnType("jsonb");

        builder.HasIndex(e => new { e.EventKind, e.CreatedAt })
            .HasDatabaseName("ix_telemetry_kind_recent")
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.TokenOrEnv, e.CreatedAt })
            .HasDatabaseName("ix_telemetry_token_recent")
            .IsDescending(false, true);
        builder.HasIndex(e => new { e.SourceFormat, e.Severity, e.CreatedAt })
            .HasDatabaseName("ix_telemetry_format_severity")
            .IsDescending(false, false, true);
        builder.HasIndex(e => e.ImportSessionId)
            .HasDatabaseName("ix_telemetry_session")
            .HasFilter("import_session_id IS NOT NULL");

        // Optional FK to session — nullable because telemetry can fire
        // outside a review (e.g. direct API import).
        builder.HasOne(e => e.Session)
            .WithMany()
            .HasForeignKey(e => e.ImportSessionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
