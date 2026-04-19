using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class ImportStructuralFindingConfiguration : IEntityTypeConfiguration<ImportStructuralFinding>
{
    public void Configure(EntityTypeBuilder<ImportStructuralFinding> builder)
    {
        builder.ToTable("import_structural_findings", t =>
        {
            // Closed vocabularies in-database — rules can't invent new kinds
            // without a migration. Keeps analytics queryable.
            t.HasCheckConstraint("ck_structural_finding_kind",
                "kind IN (" +
                "'cv_section'," +
                "'paragraph_is_cv_section'," +
                "'paragraph_as_heading'," +
                "'personal_info'," +
                "'header_table_unpack'," +
                "'spurious_toc'," +
                "'cv_class_suggestion'," +
                "'cv_list_style'," +
                "'fragmented_list'," +
                "'layout_table'," +
                "'missing_figure_caption'," +
                "'orphan_subheading_chain'" +
                ")");
            t.HasCheckConstraint("ck_structural_finding_status",
                "status IN ('pending','applied','dismissed')");
            t.HasCheckConstraint("ck_structural_finding_severity",
                "severity IN ('hint','warning','critical')");
            // Exactly one of session_id / document_id must be set.
            t.HasCheckConstraint("ck_structural_finding_owner",
                "(session_id IS NOT NULL AND document_id IS NULL) OR " +
                "(session_id IS NULL AND document_id IS NOT NULL)");
            t.HasCheckConstraint("ck_structural_finding_action",
                "action_kind IN (" +
                "'convert_block_type','set_document_class','delete_block'," +
                "'split_header_table','open_edit_modal','merge_list'" +
                ")");
        });

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.SessionId).HasColumnName("session_id");
        builder.Property(f => f.DocumentId).HasColumnName("document_id");
        builder.Property(f => f.BlockId).HasColumnName("block_id").HasMaxLength(255);
        builder.Property(f => f.Kind).HasColumnName("kind").HasMaxLength(60).IsRequired();
        builder.Property(f => f.Severity).HasColumnName("severity").HasMaxLength(20).HasDefaultValue("hint").IsRequired();
        builder.Property(f => f.Title).HasColumnName("title").HasMaxLength(500).IsRequired();
        builder.Property(f => f.Detail).HasColumnName("detail").IsRequired();
        builder.Property(f => f.SuggestedAction).HasColumnName("suggested_action").HasMaxLength(300).IsRequired();
        builder.Property(f => f.ActionKind).HasColumnName("action_kind").HasMaxLength(40).IsRequired();
        builder.Property(f => f.ActionPayload).HasColumnName("action_payload").HasColumnType("jsonb");
        builder.Property(f => f.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("pending").IsRequired();
        builder.Property(f => f.ResolvedBy).HasColumnName("resolved_by").HasMaxLength(255);
        builder.Property(f => f.ResolvedAt).HasColumnName("resolved_at");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(f => f.SessionId).HasDatabaseName("ix_structural_finding_session");
        builder.HasIndex(f => f.DocumentId).HasDatabaseName("ix_structural_finding_document");
        builder.HasIndex(f => new { f.SessionId, f.Status }).HasDatabaseName("ix_structural_finding_session_status")
            .HasFilter("status = 'pending'");
        builder.HasIndex(f => new { f.DocumentId, f.Status }).HasDatabaseName("ix_structural_finding_document_status")
            .HasFilter("status = 'pending'");
        builder.HasIndex(f => f.Kind).HasDatabaseName("ix_structural_finding_kind");

        builder.HasOne(f => f.Session)
            .WithMany(s => s.StructuralFindings)
            .HasForeignKey(f => f.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(f => f.Document)
            .WithMany()
            .HasForeignKey(f => f.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
