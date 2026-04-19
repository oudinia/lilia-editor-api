using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportDiagnosticConfiguration : IEntityTypeConfiguration<ImportDiagnostic>
{
    public void Configure(EntityTypeBuilder<ImportDiagnostic> builder)
    {
        builder.ToTable("import_diagnostics", t =>
        {
            // Closed vocabularies enforced in-database — the parser can't invent new
            // categories or severities without a migration. Keeps analytics queryable.
            t.HasCheckConstraint("ck_import_diagnostic_severity",
                "severity IN ('error','warning','info')");
            t.HasCheckConstraint("ck_import_diagnostic_category",
                "category IN ('unsupported_class','unsupported_package','load_order'," +
                "'unknown_macro','missing_asset','bibliography_unresolved'," +
                "'preamble_conflict','parse_ambiguity','auto_shimmed','size_truncated')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(d => d.BlockId).HasColumnName("block_id").HasMaxLength(255);
        builder.Property(d => d.ElementPath).HasColumnName("element_path").HasMaxLength(500);
        builder.Property(d => d.SourceLineStart).HasColumnName("source_line_start");
        builder.Property(d => d.SourceLineEnd).HasColumnName("source_line_end");
        builder.Property(d => d.SourceColStart).HasColumnName("source_col_start");
        builder.Property(d => d.SourceColEnd).HasColumnName("source_col_end");
        builder.Property(d => d.SourceSnippet).HasColumnName("source_snippet").HasMaxLength(500);
        builder.Property(d => d.Category).HasColumnName("category").HasMaxLength(40).IsRequired();
        builder.Property(d => d.Severity).HasColumnName("severity").HasMaxLength(20).HasDefaultValue("warning").IsRequired();
        builder.Property(d => d.Code).HasColumnName("code").HasMaxLength(100).IsRequired();
        builder.Property(d => d.Message).HasColumnName("message").IsRequired();
        builder.Property(d => d.SuggestedAction).HasColumnName("suggested_action");
        builder.Property(d => d.AutoFixApplied).HasColumnName("auto_fix_applied").HasDefaultValue(false);
        builder.Property(d => d.DocsUrl).HasColumnName("docs_url").HasMaxLength(500);
        builder.Property(d => d.Dismissed).HasColumnName("dismissed").HasDefaultValue(false);
        builder.Property(d => d.DismissedBy).HasColumnName("dismissed_by").HasMaxLength(255);
        builder.Property(d => d.DismissedAt).HasColumnName("dismissed_at");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(d => d.SessionId).HasDatabaseName("ix_diagnostic_session");
        builder.HasIndex(d => new { d.SessionId, d.BlockId }).HasDatabaseName("ix_diagnostic_block");
        builder.HasIndex(d => d.Code).HasDatabaseName("ix_diagnostic_code");
        // Partial index: badge counts filter out dismissed rows, so the common
        // query (active diagnostics per session by severity) stays fast without
        // scanning dismissed noise.
        builder.HasIndex(d => new { d.SessionId, d.Severity })
            .HasDatabaseName("ix_diagnostic_severity_active")
            .HasFilter("dismissed = false");

        builder.HasOne(d => d.Session)
            .WithMany(s => s.Diagnostics)
            .HasForeignKey(d => d.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
