using Lilia.Core.Entities.Epub;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Lilia.Infrastructure.Data.Configurations.Epub;

// =====================================================================
//  EPUB catalog (EPUB phase 1, 20 Nov 2026). Modelled before the code, as
//  lilia-docs/reports/2026-11-20-epub-export-audit.md asks: the exporter
//  and importer don't read these tables yet; the integrity tests do.
//  Snake-case columns and CHECK vocabularies, as the LaTeX catalog.
// =====================================================================

public class EpubSpecVersionConfiguration : IEntityTypeConfiguration<EpubSpecVersion>
{
    public void Configure(EntityTypeBuilder<EpubSpecVersion> b)
    {
        b.ToTable("epub_spec_versions", t =>
        {
            t.HasCheckConstraint("ck_epub_spec_version_family", "family IN ('epub','epub-a11y')");
            t.HasCheckConstraint("ck_epub_spec_version_status",
                "status IN ('recommendation','candidate-recommendation-draft','superseded')");
        });
        b.HasKey(x => x.Slug);
        b.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(40);
        b.Property(x => x.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        b.Property(x => x.Family).HasColumnName("family").HasMaxLength(20).IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(40).IsRequired();
        b.Property(x => x.PublishedOn).HasColumnName("published_on");
        b.Property(x => x.PackageVersion).HasColumnName("package_version").HasMaxLength(10);
        b.Property(x => x.EpubCheckVersion).HasColumnName("epubcheck_version").HasMaxLength(20);
        b.Property(x => x.IsExportTarget).HasColumnName("is_export_target").HasDefaultValue(false);
        b.Property(x => x.SortOrder).HasColumnName("sort_order");
        b.Property(x => x.SpecUrl).HasColumnName("spec_url").HasMaxLength(300);
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
    }
}

public class EpubConstructConfiguration : IEntityTypeConfiguration<EpubConstruct>
{
    public void Configure(EntityTypeBuilder<EpubConstruct> b)
    {
        b.ToTable("epub_constructs", t =>
        {
            t.HasCheckConstraint("ck_epub_construct_kind",
                "kind IN ('package-metadata','manifest-property','spine-property','nav-type','structural-semantic'," +
                "'aria-role','xhtml-element','xhtml-attribute','a11y-property','media-type','rendition-property')");
            t.HasCheckConstraint("ck_epub_construct_reader_support", "reader_support IN ('core','wide','partial','rare')");
            t.HasCheckConstraint("ck_epub_construct_fallback",
                "fallback IN ('none','alttext','altimg','text','div','span','drop','convert')");
            t.HasCheckConstraint("ck_epub_construct_coverage", "coverage_level IN ('full','partial','none','unsupported')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.Kind).HasColumnName("kind").HasMaxLength(30).IsRequired();
        b.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        b.Property(x => x.Description).HasColumnName("description");
        b.Property(x => x.ReaderSupport).HasColumnName("reader_support").HasMaxLength(10).IsRequired().HasDefaultValue("wide");
        b.Property(x => x.Fallback).HasColumnName("fallback").HasMaxLength(10).IsRequired().HasDefaultValue("none");
        b.Property(x => x.Epub2Form).HasColumnName("epub2_form").HasMaxLength(200);
        b.Property(x => x.PairedAriaRole).HasColumnName("paired_aria_role").HasMaxLength(40);
        b.Property(x => x.CoverageLevel).HasColumnName("coverage_level").HasMaxLength(20).IsRequired().HasDefaultValue("none");
        b.Property(x => x.SpecUrl).HasColumnName("spec_url").HasMaxLength(300);
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasIndex(x => new { x.Kind, x.Name }).IsUnique().HasDatabaseName("ux_epub_construct_kind_name");
        b.HasIndex(x => x.Name).HasDatabaseName("ix_epub_construct_name");
    }
}

public class EpubConstructSupportConfiguration : IEntityTypeConfiguration<EpubConstructSupport>
{
    public void Configure(EntityTypeBuilder<EpubConstructSupport> b)
    {
        b.ToTable("epub_construct_support", t =>
            t.HasCheckConstraint("ck_epub_construct_support_status",
                "status IN ('supported','legacy','outdated','deprecated','removed')"));
        b.HasKey(x => new { x.ConstructId, x.VersionSlug });
        b.Property(x => x.ConstructId).HasColumnName("construct_id");
        b.Property(x => x.VersionSlug).HasColumnName("version_slug").HasMaxLength(40);
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(x => x.Notes).HasColumnName("notes");
        b.HasOne(x => x.Construct).WithMany(c => c.Support).HasForeignKey(x => x.ConstructId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionSlug).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.VersionSlug, x.Status }).HasDatabaseName("ix_epub_construct_support_version_status");
    }
}

public class EpubBlockMappingConfiguration : IEntityTypeConfiguration<EpubBlockMapping>
{
    public void Configure(EntityTypeBuilder<EpubBlockMapping> b)
    {
        b.ToTable("epub_block_mappings", t =>
        {
            t.HasCheckConstraint("ck_epub_block_mapping_disposition", "disposition IN ('render','drop','unsupported')");
            t.HasCheckConstraint("ck_epub_block_mapping_file_split", "file_split IN ('none','starts-file')");
            t.HasCheckConstraint("ck_epub_block_mapping_nav", "nav IN ('none','toc-entry','landmark','page-list')");
            t.HasCheckConstraint("ck_epub_block_mapping_coverage", "coverage_level IN ('full','partial','none','unsupported')");
            // A block with no EPUB form says why; a rendered block names its writer.
            t.HasCheckConstraint("ck_epub_block_mapping_reason", "disposition = 'render' OR reason IS NOT NULL");
            t.HasCheckConstraint("ck_epub_block_mapping_template", "disposition <> 'render' OR template_key IS NOT NULL");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.BlockType).HasColumnName("block_type").HasMaxLength(40).IsRequired();
        b.Property(x => x.Variant).HasColumnName("variant").HasMaxLength(40).IsRequired().HasDefaultValue("");
        b.Property(x => x.Disposition).HasColumnName("disposition").HasMaxLength(20).IsRequired().HasDefaultValue("render");
        b.Property(x => x.ElementId).HasColumnName("element_id");
        b.Property(x => x.InnerElementId).HasColumnName("inner_element_id");
        b.Property(x => x.EpubTypeId).HasColumnName("epub_type_id");
        b.Property(x => x.AriaRoleId).HasColumnName("aria_role_id");
        b.Property(x => x.CssClass).HasColumnName("css_class").HasMaxLength(60);
        b.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(40);
        b.Property(x => x.Slots).HasColumnName("slots").HasColumnType("jsonb");
        b.Property(x => x.FileSplit).HasColumnName("file_split").HasMaxLength(20).IsRequired().HasDefaultValue("none");
        b.Property(x => x.Nav).HasColumnName("nav").HasMaxLength(20).IsRequired().HasDefaultValue("none");
        b.Property(x => x.ManifestProperties).HasColumnName("manifest_properties").HasColumnType("text[]");
        b.Property(x => x.Epub2ElementId).HasColumnName("epub2_element_id");
        b.Property(x => x.Epub2TemplateKey).HasColumnName("epub2_template_key").HasMaxLength(40);
        b.Property(x => x.MinVersionSlug).HasColumnName("min_version_slug").HasMaxLength(40);
        b.Property(x => x.ImportPriority).HasColumnName("import_priority").HasDefaultValue(0);
        b.Property(x => x.CoverageLevel).HasColumnName("coverage_level").HasMaxLength(20).IsRequired().HasDefaultValue("none");
        b.Property(x => x.Reason).HasColumnName("reason");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasOne(x => x.Element).WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.InnerElement).WithMany().HasForeignKey(x => x.InnerElementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EpubType).WithMany().HasForeignKey(x => x.EpubTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AriaRole).WithMany().HasForeignKey(x => x.AriaRoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Epub2Element).WithMany().HasForeignKey(x => x.Epub2ElementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.MinVersion).WithMany().HasForeignKey(x => x.MinVersionSlug).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.BlockType, x.Variant }).IsUnique().HasDatabaseName("ux_epub_block_mapping_type_variant");
    }
}

public class EpubInlineMappingConfiguration : IEntityTypeConfiguration<EpubInlineMapping>
{
    public void Configure(EntityTypeBuilder<EpubInlineMapping> b)
    {
        b.ToTable("epub_inline_mappings", t =>
        {
            t.HasCheckConstraint("ck_epub_inline_mapping_direction", "direction IN ('both','export','import')");
            t.HasCheckConstraint("ck_epub_inline_mapping_coverage", "coverage_level IN ('full','partial','none','unsupported')");
        });
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.MarkKey).HasColumnName("mark_key").HasMaxLength(40).IsRequired();
        b.Property(x => x.LmlSyntax).HasColumnName("lml_syntax").HasMaxLength(120).IsRequired();
        b.Property(x => x.Direction).HasColumnName("direction").HasMaxLength(10).IsRequired().HasDefaultValue("both");
        b.Property(x => x.ElementId).HasColumnName("element_id");
        b.Property(x => x.EpubTypeId).HasColumnName("epub_type_id");
        b.Property(x => x.AriaRoleId).HasColumnName("aria_role_id");
        b.Property(x => x.Attributes).HasColumnName("attributes").HasMaxLength(200);
        b.Property(x => x.ImportSynonyms).HasColumnName("import_synonyms").HasColumnType("text[]");
        b.Property(x => x.ManifestProperties).HasColumnName("manifest_properties").HasColumnType("text[]");
        b.Property(x => x.Epub2ElementId).HasColumnName("epub2_element_id");
        b.Property(x => x.Epub2Form).HasColumnName("epub2_form").HasMaxLength(200);
        b.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(40);
        b.Property(x => x.CoverageLevel).HasColumnName("coverage_level").HasMaxLength(20).IsRequired().HasDefaultValue("none");
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasOne(x => x.Element).WithMany().HasForeignKey(x => x.ElementId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.EpubType).WithMany().HasForeignKey(x => x.EpubTypeId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.AriaRole).WithMany().HasForeignKey(x => x.AriaRoleId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.Epub2Element).WithMany().HasForeignKey(x => x.Epub2ElementId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.MarkKey).IsUnique().HasDatabaseName("ux_epub_inline_mapping_mark_key");
    }
}

public class EpubMetadataRuleConfiguration : IEntityTypeConfiguration<EpubMetadataRule>
{
    public void Configure(EntityTypeBuilder<EpubMetadataRule> b)
    {
        b.ToTable("epub_metadata_rules", t =>
            t.HasCheckConstraint("ck_epub_metadata_rule_requirement", "requirement IN ('required','recommended','optional')"));
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        b.Property(x => x.ConstructId).HasColumnName("construct_id");
        b.Property(x => x.VersionSlug).HasColumnName("version_slug").HasMaxLength(40).IsRequired();
        b.Property(x => x.Requirement).HasColumnName("requirement").HasMaxLength(20).IsRequired();
        b.Property(x => x.Source).HasColumnName("source").HasMaxLength(120).IsRequired();
        b.Property(x => x.Expression).HasColumnName("expression").HasMaxLength(300);
        b.Property(x => x.Notes).HasColumnName("notes");
        b.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        b.HasOne(x => x.Construct).WithMany().HasForeignKey(x => x.ConstructId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Version).WithMany().HasForeignKey(x => x.VersionSlug).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.ConstructId, x.VersionSlug }).IsUnique().HasDatabaseName("ux_epub_metadata_rule_construct_version");
    }
}
