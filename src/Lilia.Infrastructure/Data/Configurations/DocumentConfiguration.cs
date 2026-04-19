using Lilia.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(d => d.OwnerId).HasColumnName("owner_id").HasMaxLength(255).IsRequired();
        builder.Property(d => d.TeamId).HasColumnName("team_id");
        builder.Property(d => d.Title).HasColumnName("title").HasMaxLength(255).HasDefaultValue("Untitled");
        builder.Property(d => d.Language).HasColumnName("language").HasMaxLength(10).HasDefaultValue("en");
        builder.Property(d => d.PaperSize).HasColumnName("paper_size").HasMaxLength(50).HasDefaultValue("a4");
        builder.Property(d => d.FontFamily).HasColumnName("font_family").HasMaxLength(100).HasDefaultValue("serif");
        builder.Property(d => d.FontSize).HasColumnName("font_size").HasDefaultValue(12);
        builder.Property(d => d.Columns).HasColumnName("columns").HasDefaultValue(1);
        builder.Property(d => d.ColumnSeparator).HasColumnName("column_separator").HasMaxLength(10).HasDefaultValue("none");
        builder.Property(d => d.ColumnGap).HasColumnName("column_gap").HasDefaultValue(1.5);
        builder.Property(d => d.IsPublic).HasColumnName("is_public").HasDefaultValue(false);
        builder.Property(d => d.ShareLink).HasColumnName("share_link").HasMaxLength(100);
        builder.Property(d => d.ShareSlug).HasColumnName("share_slug").HasMaxLength(200);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(d => d.LastOpenedAt).HasColumnName("last_opened_at");
        builder.Property(d => d.DeletedAt).HasColumnName("deleted_at");
        builder.Property(d => d.Status).HasColumnName("status").HasMaxLength(20).HasDefaultValue("draft");
        builder.Property(d => d.LastAutoSavedAt).HasColumnName("last_auto_saved_at");

        // Layout fields
        builder.Property(d => d.MarginTop).HasColumnName("margin_top");
        builder.Property(d => d.MarginBottom).HasColumnName("margin_bottom");
        builder.Property(d => d.MarginLeft).HasColumnName("margin_left");
        builder.Property(d => d.MarginRight).HasColumnName("margin_right");
        builder.Property(d => d.HeaderText).HasColumnName("header_text");
        builder.Property(d => d.FooterText).HasColumnName("footer_text");
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
    }
}
