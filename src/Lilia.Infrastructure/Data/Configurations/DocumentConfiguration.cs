using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents", t =>
        {
            t.HasCheckConstraint("ck_document_latex_engine",
                "latex_engine IN ('pdflatex','xelatex','lualatex')");
        });

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.TeamId).HasColumnName("team_id");
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(255).HasDefaultValue("Untitled");
        builder.Property(d => d.Language).HasColumnName("language").HasMaxLength(10).HasDefaultValue("en");
        builder.Property(d => d.PaperSize).HasColumnName("paper_size").HasMaxLength(50).HasDefaultValue("a4");
        builder.Property(d => d.Orientation).HasColumnName("orientation").HasMaxLength(20).HasDefaultValue("portrait");
        builder.Property(d => d.FontFamily).HasColumnName("font_family").HasMaxLength(100).HasDefaultValue("serif");
        builder.Property(d => d.FontSize).HasColumnName("font_size").HasDefaultValue(12);
        builder.Property(d => d.Columns).HasColumnName("columns").HasDefaultValue(1);
        builder.Property(d => d.ColumnSeparator).HasColumnName("column_separator").HasMaxLength(10).HasDefaultValue("none");
        builder.Property(d => d.ColumnGap).HasColumnName("column_gap").HasDefaultValue(1.5);
        builder.Property(d => d.IsPublic).HasColumnName("is_public").HasDefaultValue(false);
        builder.Property(d => d.ShareLink).HasColumnName("share_link").HasMaxLength(100);
        builder.Property(d => d.ShareSlug).HasColumnName("share_slug").HasMaxLength(200);
        // Iter 8 — public-link extensions.
        builder.Property(d => d.LinkExpiresAt).HasColumnName("link_expires_at");
        builder.Property(d => d.LinkPermission)
            .HasColumnName("link_permission")
            .HasMaxLength(20)
            .HasDefaultValue("view");
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.LastOpenedAt).HasColumnName("last_opened_at");
        builder.Property(d => d.DeletedAt).HasColumnName("deleted_at");
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("draft");
        builder.Property(d => d.LastAutoSavedAt).HasColumnName("last_auto_saved_at");
        builder.Property(d => d.IsPlayground).HasColumnName("is_playground").HasDefaultValue(false).IsRequired();
        builder.Property(d => d.CustomPreamble).HasColumnName("custom_preamble");

        // Layout fields
        builder.Property(d => d.MarginTop).HasColumnName("margin_top");
        builder.Property(d => d.MarginBottom).HasColumnName("margin_bottom");
        builder.Property(d => d.MarginLeft).HasColumnName("margin_left");
        builder.Property(d => d.MarginRight).HasColumnName("margin_right");
        builder.Property(d => d.HeaderText).HasColumnName("header_text");
        builder.Property(d => d.FooterText).HasColumnName("footer_text");
        builder.Property(d => d.HeaderLeft).HasColumnName("header_left");
        builder.Property(d => d.HeaderCenter).HasColumnName("header_center");
        builder.Property(d => d.HeaderRight).HasColumnName("header_right");
        builder.Property(d => d.FooterLeft).HasColumnName("footer_left");
        builder.Property(d => d.FooterCenter).HasColumnName("footer_center");
        builder.Property(d => d.FooterRight).HasColumnName("footer_right");
        builder.Property(d => d.LineSpacing).HasColumnName("line_spacing");
        builder.Property(d => d.ParagraphIndent).HasColumnName("paragraph_indent");
        builder.Property(d => d.PageNumbering).HasColumnName("page_numbering").HasMaxLength(20);

        // Template fields
        builder.Property(d => d.IsTemplate).HasColumnName("is_template").HasDefaultValue(false);
        builder.Property(d => d.TemplateName).HasColumnName("template_name").HasMaxLength(255);
        builder.Property(d => d.TemplateDescription).HasColumnName("template_description");
        builder.Property(d => d.TemplateCategory).HasColumnName("template_category").HasMaxLength(50);
        builder.Property(d => d.TemplateThumbnail).HasColumnName("template_thumbnail");
        builder.Property(d => d.IsPublicTemplate).HasColumnName("is_public_template").HasDefaultValue(false);
        builder.Property(d => d.TemplateUsageCount).HasColumnName("template_usage_count").HasDefaultValue(0);

        // Curated starter documents — only docs with this flag (and
        // owned by the sample-content user) get cloned to new accounts.
        builder.Property(d => d.IsStarter).HasColumnName("is_starter").HasDefaultValue(false);

        // Help content fields
        builder.Property(d => d.IsHelpContent).HasColumnName("is_help_content").HasDefaultValue(false);
        builder.Property(d => d.HelpCategory).HasColumnName("help_category").HasMaxLength(50);
        builder.Property(d => d.HelpOrder).HasColumnName("help_order").HasDefaultValue(0);
        builder.Property(d => d.HelpSlug).HasColumnName("help_slug").HasMaxLength(200);
        builder.Property(d => d.SearchText).HasColumnName("search_text");

        // Document category — drives category-specialised structural-finding
        // rules + LaTeX class selection. Null = generic detection only.
        builder.Property(d => d.DocumentCategory).HasColumnName("document_category").HasMaxLength(30);
        builder.Property(d => d.AiEnabled).HasColumnName("ai_enabled").HasDefaultValue(false);
        builder.Property(d => d.LatexEngine).HasColumnName("latex_engine").HasMaxLength(20).HasDefaultValue("pdflatex").IsRequired();

        builder.HasIndex(d => d.OwnerId);
        builder.HasIndex(d => d.TeamId);
        builder.HasIndex(d => d.ShareLink).IsUnique();

        builder.HasOne(d => d.Owner)
            .WithMany(u => u.OwnedDocuments)
            .HasForeignKey(d => d.OwnerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Team)
            .WithMany(t => t.Documents)
            .HasForeignKey(d => d.TeamId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasQueryFilter(d => d.DeletedAt == null);

        // Optimistic concurrency for the Flow editor's continuous
        // background sync. The block-sync write bumps Version and the
        // UPDATE is keyed on it (concurrency token), so a stale
        // cross-device write fails the conditional UPDATE and surfaces
        // as 409 (DbUpdateConcurrencyException → the global exception
        // filter). See architecture/2026-05-21-flow-editor-save-model.md.
        builder.Property(d => d.Version)
            .HasColumnName("version")
            .HasDefaultValue(0)
            .IsConcurrencyToken();
    }
}
