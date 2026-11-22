using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Lilia.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEpubCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "epub_constructs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    reader_support = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "wide"),
                    fallback = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "none"),
                    epub2_form = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    paired_aria_role = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    spec_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epub_constructs", x => x.id);
                    table.CheckConstraint("ck_epub_construct_coverage", "coverage_level IN ('full','partial','none','unsupported')");
                    table.CheckConstraint("ck_epub_construct_fallback", "fallback IN ('none','alttext','altimg','text','div','span','drop','convert')");
                    table.CheckConstraint("ck_epub_construct_kind", "kind IN ('package-metadata','manifest-property','spine-property','nav-type','structural-semantic','aria-role','xhtml-element','xhtml-attribute','a11y-property','media-type','rendition-property')");
                    table.CheckConstraint("ck_epub_construct_reader_support", "reader_support IN ('core','wide','partial','rare')");
                });

            migrationBuilder.CreateTable(
                name: "epub_spec_versions",
                columns: table => new
                {
                    slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    family = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    published_on = table.Column<DateOnly>(type: "date", nullable: false),
                    package_version = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    epubcheck_version = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_export_target = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    spec_url = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epub_spec_versions", x => x.slug);
                    table.CheckConstraint("ck_epub_spec_version_family", "family IN ('epub','epub-a11y')");
                    table.CheckConstraint("ck_epub_spec_version_status", "status IN ('recommendation','candidate-recommendation-draft','superseded')");
                });

            migrationBuilder.CreateTable(
                name: "epub_inline_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    mark_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    lml_syntax = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "both"),
                    element_id = table.Column<Guid>(type: "uuid", nullable: false),
                    epub_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aria_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    attributes = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    import_synonyms = table.Column<List<string>>(type: "text[]", nullable: false),
                    manifest_properties = table.Column<List<string>>(type: "text[]", nullable: false),
                    epub2_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    epub2_form = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    template_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epub_inline_mappings", x => x.id);
                    table.CheckConstraint("ck_epub_inline_mapping_coverage", "coverage_level IN ('full','partial','none','unsupported')");
                    table.CheckConstraint("ck_epub_inline_mapping_direction", "direction IN ('both','export','import')");
                    table.ForeignKey(
                        name: "FK_epub_inline_mappings_epub_constructs_aria_role_id",
                        column: x => x.aria_role_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_inline_mappings_epub_constructs_element_id",
                        column: x => x.element_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_inline_mappings_epub_constructs_epub2_element_id",
                        column: x => x.epub2_element_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_inline_mappings_epub_constructs_epub_type_id",
                        column: x => x.epub_type_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "epub_block_mappings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    block_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    variant = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false, defaultValue: ""),
                    disposition = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "render"),
                    element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inner_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    epub_type_id = table.Column<Guid>(type: "uuid", nullable: true),
                    aria_role_id = table.Column<Guid>(type: "uuid", nullable: true),
                    css_class = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    template_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    slots = table.Column<string>(type: "jsonb", nullable: true),
                    file_split = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    nav = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    manifest_properties = table.Column<List<string>>(type: "text[]", nullable: false),
                    epub2_element_id = table.Column<Guid>(type: "uuid", nullable: true),
                    epub2_template_key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    min_version_slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    import_priority = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    coverage_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "none"),
                    reason = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epub_block_mappings", x => x.id);
                    table.CheckConstraint("ck_epub_block_mapping_coverage", "coverage_level IN ('full','partial','none','unsupported')");
                    table.CheckConstraint("ck_epub_block_mapping_disposition", "disposition IN ('render','drop','unsupported')");
                    table.CheckConstraint("ck_epub_block_mapping_file_split", "file_split IN ('none','starts-file')");
                    table.CheckConstraint("ck_epub_block_mapping_nav", "nav IN ('none','toc-entry','landmark','page-list')");
                    table.CheckConstraint("ck_epub_block_mapping_reason", "disposition = 'render' OR reason IS NOT NULL");
                    table.CheckConstraint("ck_epub_block_mapping_template", "disposition <> 'render' OR template_key IS NOT NULL");
                    table.ForeignKey(
                        name: "FK_epub_block_mappings_epub_constructs_aria_role_id",
                        column: x => x.aria_role_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_block_mappings_epub_constructs_element_id",
                        column: x => x.element_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_block_mappings_epub_constructs_epub2_element_id",
                        column: x => x.epub2_element_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_block_mappings_epub_constructs_epub_type_id",
                        column: x => x.epub_type_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_block_mappings_epub_constructs_inner_element_id",
                        column: x => x.inner_element_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_epub_block_mappings_epub_spec_versions_min_version_slug",
                        column: x => x.min_version_slug,
                        principalTable: "epub_spec_versions",
                        principalColumn: "slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "epub_construct_support",
                columns: table => new
                {
                    construct_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epub_construct_support", x => new { x.construct_id, x.version_slug });
                    table.CheckConstraint("ck_epub_construct_support_status", "status IN ('supported','legacy','outdated','deprecated','removed')");
                    table.ForeignKey(
                        name: "FK_epub_construct_support_epub_constructs_construct_id",
                        column: x => x.construct_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_epub_construct_support_epub_spec_versions_version_slug",
                        column: x => x.version_slug,
                        principalTable: "epub_spec_versions",
                        principalColumn: "slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "epub_metadata_rules",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    construct_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_slug = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    requirement = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    expression = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()"),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "NOW()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_epub_metadata_rules", x => x.id);
                    table.CheckConstraint("ck_epub_metadata_rule_requirement", "requirement IN ('required','recommended','optional')");
                    table.ForeignKey(
                        name: "FK_epub_metadata_rules_epub_constructs_construct_id",
                        column: x => x.construct_id,
                        principalTable: "epub_constructs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_epub_metadata_rules_epub_spec_versions_version_slug",
                        column: x => x.version_slug,
                        principalTable: "epub_spec_versions",
                        principalColumn: "slug",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_epub_block_mappings_aria_role_id",
                table: "epub_block_mappings",
                column: "aria_role_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_block_mappings_element_id",
                table: "epub_block_mappings",
                column: "element_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_block_mappings_epub_type_id",
                table: "epub_block_mappings",
                column: "epub_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_block_mappings_epub2_element_id",
                table: "epub_block_mappings",
                column: "epub2_element_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_block_mappings_inner_element_id",
                table: "epub_block_mappings",
                column: "inner_element_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_block_mappings_min_version_slug",
                table: "epub_block_mappings",
                column: "min_version_slug");

            migrationBuilder.CreateIndex(
                name: "ux_epub_block_mapping_type_variant",
                table: "epub_block_mappings",
                columns: new[] { "block_type", "variant" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_epub_construct_support_version_status",
                table: "epub_construct_support",
                columns: new[] { "version_slug", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_epub_construct_name",
                table: "epub_constructs",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ux_epub_construct_kind_name",
                table: "epub_constructs",
                columns: new[] { "kind", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_epub_inline_mappings_aria_role_id",
                table: "epub_inline_mappings",
                column: "aria_role_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_inline_mappings_element_id",
                table: "epub_inline_mappings",
                column: "element_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_inline_mappings_epub_type_id",
                table: "epub_inline_mappings",
                column: "epub_type_id");

            migrationBuilder.CreateIndex(
                name: "IX_epub_inline_mappings_epub2_element_id",
                table: "epub_inline_mappings",
                column: "epub2_element_id");

            migrationBuilder.CreateIndex(
                name: "ux_epub_inline_mapping_mark_key",
                table: "epub_inline_mappings",
                column: "mark_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_epub_metadata_rules_version_slug",
                table: "epub_metadata_rules",
                column: "version_slug");

            migrationBuilder.CreateIndex(
                name: "ux_epub_metadata_rule_construct_version",
                table: "epub_metadata_rules",
                columns: new[] { "construct_id", "version_slug" },
                unique: true);

            // EPUB phase 1 (20 Nov 2026): the seed, from lilia-docs/reports/2026-11-20-epub-export-audit.md
            // (its tables, the EPUBCheck 5.4.0 findings and the specifications it read). Nothing reads these rows
            // but the integrity tests until phases 2 and 3; the exporter and importer are unchanged.
            migrationBuilder.Sql(SeedVersions);
            migrationBuilder.Sql(SeedConstructs);
            migrationBuilder.Sql(SeedBlockMappings);
            migrationBuilder.Sql(SeedInlineMappings);
            migrationBuilder.Sql(SeedMetadataRules);
        }

        private const string SeedVersions = @"
INSERT INTO epub_spec_versions (slug, title, family, status, published_on, package_version, epubcheck_version, is_export_target, sort_order, spec_url, notes) VALUES
  ('epub-2.0.1',    'EPUB 2.0.1',             'epub',      'superseded',                     '2010-09-04', '2.0', '5.4.0', true,  10, 'https://idpf.org/epub/201', 'The ""older readers"" target: OPF 2.0.1, XHTML 1.1, NCX and guide; no MathML, no epub:type.'),
  ('epub-3.0',      'EPUB 3.0',               'epub',      'superseded',                     '2011-10-11', '3.0', NULL,    false, 20, 'https://idpf.org/epub/30/', 'IDPF Recommendation; the oldest EPUB 3 readers implement it. EPUBCheck checks every EPUB 3 file against the latest 3.x.'),
  ('epub-3.3',      'EPUB 3.3',               'epub',      'recommendation',                 '2026-01-13', '3.0', NULL,    false, 30, 'https://www.w3.org/TR/epub-33/', 'W3C Recommendation, republished 13 Jan 2026.'),
  ('epub-3.4',      'EPUB 3.4',               'epub',      'candidate-recommendation-draft', '2026-11-19', '3.0', '5.4.0', true,  40, 'https://www.w3.org/TR/epub-34/', 'The default target. A 3.4 file still writes version=3.0: each 3.x continues EPUB 3.'),
  ('epub-a11y-1.1', 'EPUB Accessibility 1.1', 'epub-a11y', 'recommendation',                 '2023-05-25', NULL,  NULL,    false, 50, 'https://www.w3.org/TR/epub-a11y-11/', NULL),
  ('epub-a11y-1.2', 'EPUB Accessibility 1.2', 'epub-a11y', 'candidate-recommendation-draft', '2026-11-11', NULL,  NULL,    true,  60, 'https://www.w3.org/TR/epub-a11y-12/', 'accessModeSufficient becomes required and accessMode recommended.');
";

        // One row per construct; the six columns after the EPUB 2 form are its state in EPUB 2.0.1, 3.0, 3.3,
        // 3.4, Accessibility 1.1 and 1.2: s supported, l legacy, o outdated, d deprecated, r removed, NULL absent.
        private const string SeedConstructs = @"
DROP TABLE IF EXISTS epub_seed_construct;
CREATE TEMP TABLE epub_seed_construct (
  kind text, name text, reader_support text, fallback text, epub2_form text, paired_aria_role text,
  v201 text, v30 text, v33 text, v34 text, a11 text, a12 text, notes text);

INSERT INTO epub_seed_construct VALUES
  -- Package metadata (Dublin Core and OPF)
  ('package-metadata', 'dc:identifier',         'core',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, 'The unique identifier. Today a new one is made on every export, so readers treat each export as another book.'),
  ('package-metadata', 'dc:title',              'core',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:language',           'core',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, 'Today every book is declared en: the web app sends only the title.'),
  ('package-metadata', 'dc:creator',            'core',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:contributor',        'wide',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:publisher',          'wide',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:date',               'core',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, 'EPUB 3: the publication date only; EPUB 2 dates by opf:event.'),
  ('package-metadata', 'dc:subject',            'wide',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:description',        'wide',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:rights',             'wide',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:source',             'partial', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, 'The print edition a page list follows.'),
  ('package-metadata', 'dc:type',               'partial', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:format',             'rare',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:relation',           'rare',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dc:coverage',           'rare',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'dcterms:modified',      'core',    'none', 'dc:date opf:event=modification', NULL, NULL, 's', 's', 's', NULL, NULL, 'Required in every EPUB 3 package.'),
  ('package-metadata', 'meta-property',         'core',    'none', 'meta name/content', NULL, NULL, 's', 's', 's', NULL, NULL, 'The OPF 3 meta element (property attribute).'),
  ('package-metadata', 'meta-name-content',     'wide',    'none', NULL, NULL, 's', 's', 'l', 'o', NULL, NULL, 'The OPF 2 meta element: legacy in 3.3, outdated in 3.4.'),
  ('package-metadata', 'meta-name-cover',       'wide',    'none', NULL, NULL, 's', 's', 'l', 'o', NULL, NULL, 'The EPUB 2 cover declaration; EPUB 3 uses the cover-image manifest property.'),
  ('package-metadata', 'refines',               'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'The attribute that attaches a meta to another element (role, file-as).'),
  ('package-metadata', 'role',                  'wide',    'none', 'opf:role', NULL, NULL, 's', 's', 's', NULL, NULL, 'MARC relator code refining a dc:creator (aut, edt).'),
  ('package-metadata', 'file-as',               'wide',    'none', 'opf:file-as', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'display-seq',           'partial', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'title-type',            'partial', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'identifier-type',       'partial', 'none', 'opf:scheme', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'alternate-script',      'partial', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('package-metadata', 'belongs-to-collection', 'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'From EPUB 3.0.1.'),
  ('package-metadata', 'collection-type',       'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'From EPUB 3.0.1.'),
  ('package-metadata', 'group-position',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'From EPUB 3.0.1.'),
  ('package-metadata', 'meta-auth',             'rare',    'none', NULL, NULL, NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated since EPUB 3.1.'),
  ('package-metadata', 'source-of',             'rare',    'none', NULL, NULL, NULL, NULL, 's', 'd', NULL, NULL, 'From EPUB 3.0.1; the 3.4 draft replaces it with pageBreakSource.'),
  ('package-metadata', 'pageBreakSource',       'rare',    'none', NULL, NULL, NULL, NULL, NULL, 's', NULL, NULL, 'New in the 3.4 draft: names the print edition a page list follows. Ignored by older readers.'),
  ('package-metadata', 'opf:role',              'wide',    'none', NULL, NULL, 's', 'r', 'r', 'r', NULL, NULL, 'EPUB 2 attribute on dc:creator; EPUB 3 refines with role.'),
  ('package-metadata', 'opf:file-as',           'wide',    'none', NULL, NULL, 's', 'r', 'r', 'r', NULL, NULL, 'EPUB 2 attribute; EPUB 3 refines with file-as.'),
  ('package-metadata', 'opf:scheme',            'wide',    'none', NULL, NULL, 's', 'r', 'r', 'r', NULL, NULL, 'EPUB 2 attribute on dc:identifier; EPUB 3 refines with identifier-type.'),
  ('package-metadata', 'opf:event',             'wide',    'none', NULL, NULL, 's', 'r', 'r', 'r', NULL, NULL, 'EPUB 2 dates by event (publication, modification).'),
  ('package-metadata', 'link',                  'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'The OPF 3 link element (rel=record and others).'),

  -- Manifest properties and attributes (EPUB 3)
  ('manifest-property', 'nav',              'core',    'none',    'NCX navMap', NULL, NULL, 's', 's', 's', NULL, NULL, 'Exactly one navigation document.'),
  ('manifest-property', 'cover-image',      'wide',    'none',    'meta name=cover', NULL, NULL, 's', 's', 's', NULL, NULL, 'Today the cover is an img on the first page, not declared, so shelves show no cover.'),
  ('manifest-property', 'mathml',           'wide',    'alttext', 'PNG img with alt text', NULL, NULL, 's', 's', 's', NULL, NULL, 'Apple Books, Kobo, Thorium and Google Play Books render MathML.'),
  ('manifest-property', 'svg',              'wide',    'none',    NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('manifest-property', 'scripted',         'partial', 'none',    NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'Lilia writes no scripts.'),
  ('manifest-property', 'remote-resources', 'partial', 'none',    NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'Lilia never writes it: images are packed into the book (EPUBCheck RSC-006 and OPF-014 today).'),
  ('manifest-property', 'switch',           'rare',    'none',    NULL, NULL, NULL, 's', 'd', 'd', NULL, NULL, 'epub:switch, deprecated since EPUB 3.1.'),
  ('manifest-property', 'fallback',         'partial', 'none',    NULL, NULL, 's', 's', 's', 'o', NULL, NULL, 'Manifest content fallbacks (the fallback attribute): outdated in 3.4.'),

  -- Spine
  ('spine-property', 'page-progression-direction', 'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'rtl for Arabic and Hebrew: today an Arabic book turns its pages left to right.'),
  ('spine-property', 'linear',                     'core',    'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('spine-property', 'toc',                        'core',    'none', NULL, NULL, 's', 's', 'l', 'o', NULL, NULL, 'The spine attribute naming the NCX.'),
  ('spine-property', 'page-spread-left',           'partial', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('spine-property', 'page-spread-right',          'partial', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),

  -- Navigation
  ('nav-type', 'toc',       'core',    'none', 'NCX navMap', NULL, NULL, 's', 's', 's', NULL, NULL, 'Today only level-1 headings reach it.'),
  ('nav-type', 'landmarks', 'wide',    'none', 'guide', NULL, NULL, 's', 's', 's', NULL, NULL, 'A landmark may point at the navigation file only if that file is in the reading order (EPUBCheck RSC-011).'),
  ('nav-type', 'page-list', 'partial', 'none', 'NCX pageList', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('nav-type', 'lot',       'rare',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'List of tables.'),
  ('nav-type', 'loi',       'rare',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'List of illustrations.'),
  ('nav-type', 'ncx',       'core',    'none', NULL, NULL, 's', 's', 'l', 'o', NULL, NULL, 'The EPUB 2 table of contents (toc.ncx). Today it names the book bookid, so every file fails EPUBCheck (NCX-001).'),
  ('nav-type', 'guide',     'wide',    'none', NULL, NULL, 's', 'd', 'l', 'o', NULL, NULL, 'The EPUB 2 landmarks.'),

  -- epub:type values (Structural Semantics Vocabulary; EPUB 3 only)
  ('structural-semantic', 'cover',           'wide',    'none', NULL, 'doc-cover',           NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'frontmatter',     'wide',    'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'bodymatter',      'wide',    'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'backmatter',      'wide',    'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'chapter',         'wide',    'none', NULL, 'doc-chapter',         NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'part',            'wide',    'none', NULL, 'doc-part',            NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'abstract',        'partial', 'none', NULL, 'doc-abstract',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'preface',         'partial', 'none', NULL, 'doc-preface',         NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'prologue',        'partial', 'none', NULL, 'doc-prologue',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'epilogue',        'partial', 'none', NULL, 'doc-epilogue',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'introduction',    'partial', 'none', NULL, 'doc-introduction',    NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'conclusion',      'partial', 'none', NULL, 'doc-conclusion',      NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'foreword',        'partial', 'none', NULL, 'doc-foreword',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'afterword',       'partial', 'none', NULL, 'doc-afterword',       NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'appendix',        'partial', 'none', NULL, 'doc-appendix',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'glossary',        'partial', 'none', NULL, 'doc-glossary',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'glossterm',       'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'glossdef',        'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'glossref',        'partial', 'none', NULL, 'doc-glossref',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'bibliography',    'wide',    'none', NULL, 'doc-bibliography',    NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'biblioentry',     'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, 'No paired role: doc-biblioentry is deprecated (EPUBCheck RSC-017).'),
  ('structural-semantic', 'biblioref',       'partial', 'none', NULL, 'doc-biblioref',       NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'index',           'partial', 'none', NULL, 'doc-index',           NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'toc',             'core',    'none', NULL, 'doc-toc',             NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'landmarks',       'wide',    'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'page-list',       'partial', 'none', NULL, 'doc-pagelist',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'pagebreak',       'partial', 'none', NULL, 'doc-pagebreak',       NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'footnote',        'wide',    'none', NULL, 'doc-footnote',        NULL, 's', 's', 's', NULL, NULL, 'Pop-up notes in Apple Books and Kobo.'),
  ('structural-semantic', 'footnotes',       'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'endnote',         'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, 'No paired role: doc-endnote is deprecated.'),
  ('structural-semantic', 'endnotes',        'partial', 'none', NULL, 'doc-endnotes',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'noteref',         'wide',    'none', NULL, 'doc-noteref',         NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'backlink',        'partial', 'none', NULL, 'doc-backlink',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'titlepage',       'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'halftitlepage',   'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'copyright-page',  'partial', 'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'dedication',      'partial', 'none', NULL, 'doc-dedication',      NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'epigraph',        'partial', 'none', NULL, 'doc-epigraph',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'acknowledgments', 'partial', 'none', NULL, 'doc-acknowledgments', NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'colophon',        'partial', 'none', NULL, 'doc-colophon',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'errata',          'rare',    'none', NULL, 'doc-errata',          NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'credits',         'rare',    'none', NULL, 'doc-credits',         NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'credit',          'rare',    'none', NULL, 'doc-credit',          NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'loi',             'rare',    'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'lot',             'rare',    'none', NULL, NULL,                  NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'pullquote',       'rare',    'none', NULL, 'doc-pullquote',       NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'qna',             'rare',    'none', NULL, 'doc-qna',             NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'tip',             'rare',    'none', NULL, 'doc-tip',             NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'notice',          'rare',    'none', NULL, 'doc-notice',          NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'example',         'rare',    'none', NULL, 'doc-example',         NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'subtitle',        'rare',    'none', NULL, 'doc-subtitle',        NULL, 's', 's', 's', NULL, NULL, NULL),
  ('structural-semantic', 'annotation',      'partial', 'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated: EPUBCheck OPF-086b on today''s annotation block.'),
  ('structural-semantic', 'note',            'partial', 'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated term.'),
  ('structural-semantic', 'help',            'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated term.'),
  ('structural-semantic', 'marginalia',      'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated term.'),
  ('structural-semantic', 'rearnote',        'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated: use endnote.'),
  ('structural-semantic', 'rearnotes',       'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated: use endnotes.'),
  ('structural-semantic', 'sidebar',         'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated term.'),
  ('structural-semantic', 'subchapter',      'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated term.'),
  ('structural-semantic', 'warning',         'rare',    'none', NULL, NULL,                  NULL, 's', 'd', 'd', NULL, NULL, 'Deprecated term.'),

  -- ARIA roles: DPUB-ARIA (doc-*) from EPUB 3.3, and the core ARIA roles Lilia writes
  ('aria-role', 'doc-abstract',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-acknowledgments', 'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-afterword',       'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-appendix',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-backlink',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-biblioentry',     'partial', 'none', NULL, NULL, NULL, NULL, 'd', 'd', NULL, NULL, 'Deprecated in DPUB-ARIA 1.1 (EPUBCheck RSC-017): write epub:type biblioentry only.'),
  ('aria-role', 'doc-bibliography',    'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-biblioref',       'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-chapter',         'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-colophon',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-conclusion',      'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-cover',           'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'On the cover img.'),
  ('aria-role', 'doc-credit',          'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-credits',         'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-dedication',      'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-endnote',         'partial', 'none', NULL, NULL, NULL, NULL, 'd', 'd', NULL, NULL, 'Deprecated in DPUB-ARIA 1.1.'),
  ('aria-role', 'doc-endnotes',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-epigraph',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-epilogue',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-errata',          'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-example',         'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-footnote',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-foreword',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-glossary',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-glossref',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-index',           'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-introduction',    'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-noteref',         'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-notice',          'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-pagebreak',       'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-pagefooter',      'rare',    'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'New in DPUB-ARIA 1.1.'),
  ('aria-role', 'doc-pageheader',      'rare',    'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'New in DPUB-ARIA 1.1.'),
  ('aria-role', 'doc-pagelist',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-part',            'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-preface',         'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-prologue',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-pullquote',       'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-qna',             'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-subtitle',        'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-tip',             'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'doc-toc',             'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('aria-role', 'group',               'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'note',                'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'math',                'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'figure',              'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'img',                 'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'presentation',        'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'region',              'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('aria-role', 'navigation',          'wide',    'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),

  -- XHTML elements: XHTML 1.1 for EPUB 2, HTML (XHTML syntax only, from 3.4) for EPUB 3
  ('xhtml-element', 'p',          'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'div',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'span',       'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'a',          'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'em',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'strong',     'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'b',          'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'i',          'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'code',       'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'kbd',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'samp',       'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'var',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'dfn',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'abbr',       'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'cite',       'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'q',          'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'small',      'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'sub',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'sup',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'del',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'ins',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'br',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'hr',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'img',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'pre',        'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'blockquote', 'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'h1',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'h2',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'h3',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'h4',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'h5',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'h6',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'ol',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'ul',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'li',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'dl',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'dt',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'dd',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'table',      'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'caption',    'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'thead',      'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'tbody',      'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'tfoot',      'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'tr',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'th',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'td',         'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'svg',        'wide', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, 'Inline SVG; OPS 2.0.1 allows it in XHTML.'),
  ('xhtml-element', 'section',    'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, 'Not in XHTML 1.1 (EPUBCheck RSC-005 when relabelled as EPUB 2).'),
  ('xhtml-element', 'article',    'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'aside',      'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, 'Not in XHTML 1.1.'),
  ('xhtml-element', 'nav',        'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'figure',     'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, 'Not in XHTML 1.1.'),
  ('xhtml-element', 'figcaption', 'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'header',     'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'footer',     'core', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'main',       'wide', 'div',  'div', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'mark',       'wide', 'span', 'span', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 's',          'core', 'span', 'del', NULL, NULL, 's', 's', 's', NULL, NULL, 'Not in XHTML 1.1.'),
  ('xhtml-element', 'u',          'core', 'span', 'span', NULL, NULL, 's', 's', 's', NULL, NULL, 'Not in XHTML 1.1.'),
  ('xhtml-element', 'time',       'wide', 'span', 'span', NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-element', 'math',       'wide', 'alttext', 'PNG img with alt text, or Unicode text', NULL, NULL, 's', 's', 's', NULL, NULL, 'MathML, written from KaTeX with an annotation holding the LaTeX.'),

  -- XHTML attributes
  ('xhtml-attribute', 'xml:lang',  'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-attribute', 'lang',      'core', 'none', 'xml:lang', NULL, NULL, 's', 's', 's', NULL, NULL, 'Today no page has lang or dir.'),
  ('xhtml-attribute', 'dir',       'core', 'none', NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-attribute', 'epub:type', 'wide', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-attribute', 'role',      'wide', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, NULL),
  ('xhtml-attribute', 'alttext',   'wide', 'none', NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'MathML attribute: the spoken form, for readers without MathML.'),
  ('xhtml-attribute', 'translate', 'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'translate=no on code and LaTeX source; 3.4 adds the other ITS attributes.'),

  -- Accessibility metadata (schema.org and a11y vocabularies)
  ('a11y-property', 'schema:accessMode',            'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', 'Recommended by Accessibility 1.2.'),
  ('a11y-property', 'schema:accessModeSufficient',  'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', 'Required by Accessibility 1.2.'),
  ('a11y-property', 'schema:accessibilityFeature',  'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', NULL),
  ('a11y-property', 'schema:accessibilityHazard',   'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', NULL),
  ('a11y-property', 'schema:accessibilitySummary',  'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', NULL),
  ('a11y-property', 'dcterms:conformsTo',           'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', 'The conformance claim: only after an accessibility check passes.'),
  ('a11y-property', 'a11y:certifiedBy',             'partial', 'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', NULL),
  ('a11y-property', 'a11y:certifierCredential',     'rare',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', NULL),
  ('a11y-property', 'a11y:certifierReport',         'rare',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', NULL),
  ('a11y-property', 'a11y:exemption',               'rare',    'none', NULL, NULL, NULL, NULL, 's', 's', 's', 's', 'An exemption under the European Accessibility Act.'),
  ('a11y-property', 'a11y:contactEmail',            'rare',    'none', NULL, NULL, NULL, NULL, NULL, 's', NULL, 's', 'New in Accessibility 1.2.'),

  -- Core media types
  ('media-type', 'application/xhtml+xml',    'core',    'none',    NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('media-type', 'application/x-dtbncx+xml', 'core',    'none',    NULL, NULL, 's', 's', 'l', 'o', NULL, NULL, 'The NCX.'),
  ('media-type', 'text/css',                 'core',    'none',    NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('media-type', 'image/gif',                'core',    'none',    NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('media-type', 'image/jpeg',               'core',    'none',    NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('media-type', 'image/png',                'core',    'none',    NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('media-type', 'image/svg+xml',            'core',    'none',    NULL, NULL, 's', 's', 's', 's', NULL, NULL, NULL),
  ('media-type', 'image/webp',               'partial', 'convert', 'image/png', NULL, NULL, NULL, 's', 's', NULL, NULL, 'Core from EPUB 3.3.'),
  ('media-type', 'image/avif',               'rare',    'convert', 'image/png', NULL, NULL, NULL, NULL, 's', NULL, NULL, 'Core only from 3.4; older readers don''t show it: convert to PNG or JPEG (SkiaSharp).'),
  ('media-type', 'image/jxl',                'rare',    'convert', 'image/png', NULL, NULL, NULL, NULL, 's', NULL, NULL, 'JPEG XL: core only from 3.4; convert to PNG or JPEG.'),
  ('media-type', 'font/otf',                 'wide',    'drop',    NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'EPUB 3.0 named it application/vnd.ms-opentype.'),
  ('media-type', 'font/woff',                'wide',    'drop',    NULL, NULL, NULL, 's', 's', 's', NULL, NULL, 'EPUB 3.0 named it application/font-woff.'),
  ('media-type', 'font/woff2',               'wide',    'drop',    NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),
  ('media-type', 'font/ttf',                 'wide',    'drop',    NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, NULL),

  -- Rendition (layout) properties: from EPUB 3.0.1 (Fixed Layout 1.0). Lilia books are reflowable.
  ('rendition-property', 'rendition:layout',         'wide',    'none', NULL, NULL, NULL, NULL, 's', 's', NULL, NULL, 'reflowable (the default) or pre-paginated.'),
  ('rendition-property', 'rendition:orientation',    'partial', 'none', NULL, NULL, NULL, NULL, 's', 'o', NULL, NULL, NULL),
  ('rendition-property', 'rendition:spread',         'partial', 'none', NULL, NULL, NULL, NULL, 's', 'o', NULL, NULL, NULL),
  ('rendition-property', 'rendition:flow',           'partial', 'none', NULL, NULL, NULL, NULL, 's', 'o', NULL, NULL, NULL),
  ('rendition-property', 'rendition:align-x-center', 'rare',    'none', NULL, NULL, NULL, NULL, 's', 'd', NULL, NULL, NULL),
  ('rendition-property', 'rendition:viewport',       'rare',    'none', NULL, NULL, NULL, NULL, 'd', 'd', NULL, NULL, NULL);

INSERT INTO epub_constructs (kind, name, reader_support, fallback, epub2_form, paired_aria_role, notes)
SELECT kind, name, reader_support, fallback, epub2_form, paired_aria_role, notes FROM epub_seed_construct;

-- An unknown code leaves the status NULL and fails the NOT NULL: a typo can't slip in silently.
INSERT INTO epub_construct_support (construct_id, version_slug, status)
SELECT c.id, v.slug,
       CASE v.code WHEN 's' THEN 'supported' WHEN 'l' THEN 'legacy' WHEN 'o' THEN 'outdated'
                   WHEN 'd' THEN 'deprecated' WHEN 'r' THEN 'removed' END
FROM epub_seed_construct s
JOIN epub_constructs c ON c.kind = s.kind AND c.name = s.name
CROSS JOIN LATERAL (VALUES ('epub-2.0.1', s.v201), ('epub-3.0', s.v30), ('epub-3.3', s.v33), ('epub-3.4', s.v34),
                           ('epub-a11y-1.1', s.a11), ('epub-a11y-1.2', s.a12)) AS v(slug, code)
WHERE v.code IS NOT NULL;

DROP TABLE epub_seed_construct;
";

        // Every BlockTypes constant (the 40 in All, slide, and the legacy quote / image / divider), from the
        // audit's block-by-block table and its seed rows. Constructs are named here and resolved to ids below;
        // a name the catalog lacks stops the migration.
        private const string SeedBlockMappings = @"
DROP TABLE IF EXISTS epub_seed_block;
CREATE TEMP TABLE epub_seed_block (
  block_type text, variant text, disposition text, element text, inner_element text, epub_type text, aria_role text,
  css_class text, template_key text, slots text, file_split text, nav text, manifest_properties text[],
  epub2_element text, epub2_template_key text, import_priority int, reason text, notes text);

INSERT INTO epub_seed_block VALUES
  ('heading', 'level-1', 'render', 'section', 'h1', 'chapter', 'doc-chapter', NULL, 'heading', '{""text"":""inline"",""label"":""id"",""level"":""h1""}', 'starts-file', 'toc-entry', NULL, 'div', NULL, 20, NULL,
   'A level-1 heading opens a chapter file; EPUB 2 writes div class=chapter and an NCX navPoint.'),
  ('heading', '', 'render', 'section', 'h2', NULL, NULL, NULL, 'heading', '{""text"":""inline"",""label"":""id"",""level"":""h2-h6""}', 'none', 'toc-entry', NULL, 'div', NULL, 10, NULL,
   'Levels 2 to 6, nested in the contents. Today only level 1 reaches it, a level stored as text becomes h1, and the label is dropped.'),
  ('paragraph', '', 'render', 'p', NULL, NULL, NULL, NULL, 'paragraph', '{""text"":""inline""}', 'none', 'none', NULL, 'p', NULL, 10, NULL,
   'Inline marks through epub_inline_mappings; today they print as source.'),
  ('equation', '', 'render', 'div', 'math', NULL, NULL, 'equation', 'equation', '{""latex"":""mathml"",""label"":""id"",""number"":""span.eq-number""}', 'none', 'none', ARRAY['mathml'], 'div', 'equation-image', 10, NULL,
   'MathML from KaTeX with alttext; EPUB 2: a PNG of the formula with alt text, or Unicode text for a simple one. Today raw LaTeX.'),
  ('equation', 'align', 'render', 'div', 'math', NULL, NULL, 'equation align', 'equation', '{""latex"":""mathml-aligned"",""label"":""id"",""number"":""span.eq-number""}', 'none', 'none', ARRAY['mathml'], 'div', 'equation-image', 10, NULL,
   'align and gather bodies, as an aligned table in one math element.'),
  ('figure', '', 'render', 'figure', 'img', NULL, NULL, NULL, 'figure', '{""src"":""asset"",""alt"":""alt"",""caption"":""figcaption"",""label"":""id""}', 'none', 'none', NULL, 'div', NULL, 10, NULL,
   'Images packed into the book (EPUB/images/), never left on our server. A missing alt text is a warning, never a silent empty alt.'),
  ('table', '', 'render', 'table', NULL, NULL, NULL, NULL, 'table', '{""headers"":""thead"",""rows"":""tbody"",""caption"":""caption"",""label"":""id""}', 'none', 'none', NULL, 'table', NULL, 10, NULL,
   'Tables-tool cells ({content}) included: today they crash the export.'),
  ('code', '', 'render', 'pre', 'code', NULL, NULL, NULL, 'code', '{""code"":""text"",""language"":""class"",""caption"":""caption""}', 'none', 'none', NULL, 'pre', NULL, 10, NULL, NULL),
  ('list', '', 'render', 'ul', 'li', NULL, NULL, NULL, 'list', '{""items"":""li"",""nested"":""list""}', 'none', 'none', NULL, 'ul', NULL, 10, NULL,
   'A nested item is a list inside its li: today object items crash the export.'),
  ('list', 'numbered', 'render', 'ol', 'li', NULL, NULL, NULL, 'list', '{""items"":""li"",""start"":""start"",""labelFormat"":""type""}', 'none', 'none', NULL, 'ol', NULL, 10, NULL,
   'kind numbered, or ordered true (also stored as text, which crashes today''s export).'),
  ('blockquote', '', 'render', 'blockquote', NULL, NULL, NULL, NULL, 'blockquote', '{""text"":""inline"",""attribution"":""footer""}', 'none', 'none', NULL, 'blockquote', NULL, 10, NULL, NULL),
  ('blockquote', 'verse', 'render', 'blockquote', NULL, NULL, NULL, 'verse', 'verse', '{""text"":""lines"",""attribution"":""footer""}', 'none', 'none', NULL, 'blockquote', NULL, 10, NULL, NULL),
  ('theorem', '', 'render', 'div', NULL, NULL, 'group', 'theorem', 'theorem', '{""theoremType"":""heading"",""title"":""heading"",""number"":""heading"",""label"":""id"",""text"":""inline""}', 'none', 'none', NULL, 'div', NULL, 10, NULL,
   'Writes Theorem 1 (Title) as the group''s heading: today only the body comes out.'),
  ('theorem', 'proof', 'render', 'div', NULL, NULL, 'group', 'proof', 'theorem', '{""text"":""inline"",""qed"":""span.qed""}', 'none', 'none', NULL, 'div', NULL, 10, NULL, NULL),
  ('abstract', '', 'render', 'section', NULL, 'abstract', 'doc-abstract', 'abstract', 'abstract', '{""text"":""inline"",""heading"":""h2""}', 'none', 'none', NULL, 'div', NULL, 10, NULL,
   'The heading in the document''s language (always English today).'),
  ('title', '', 'render', 'section', 'h1', 'titlepage', NULL, 'titlepage', 'title-page', '{""title"":""h1"",""subtitle"":""p"",""authors"":""p"",""date"":""p""}', 'starts-file', 'none', NULL, 'div', NULL, 10, NULL,
   'Today the title block comes out as an empty paragraph.'),
  ('bibliography', '', 'render', 'section', 'ol', 'bibliography', 'doc-bibliography', NULL, 'bibliography', '{""entries"":""BibliographyEntries"",""entry"":""li#bib-{key}""}', 'starts-file', 'landmark', NULL, 'div', NULL, 10, NULL,
   'Entries from the BibliographyEntries table (never read today); each li has epub:type biblioentry only, as doc-biblioentry is deprecated.'),
  ('tableOfContents', '', 'render', 'nav', 'ol', 'toc', 'doc-toc', NULL, 'toc', '{""title"":""h2""}', 'starts-file', 'landmark', NULL, NULL, 'drop', 0, NULL,
   'Puts the navigation document in the reading order here, so the toc landmark may point at it (RSC-011). EPUB 2: the NCX replaces it.'),
  ('pageBreak', '', 'render', 'span', NULL, 'pagebreak', 'doc-pagebreak', NULL, 'page-marker', '{""number"":""title""}', 'none', 'page-list', NULL, NULL, 'drop', 0, NULL,
   'A page marker for the page list, invisible in the text. Today a stray empty paragraph.'),
  ('columnBreak', '', 'drop', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0,
   'A reflowable book has no columns, so nothing is written.', 'Today a stray empty paragraph.'),
  ('columnLayout', '', 'drop', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0,
   'A reflowable book has no columns: the layout is dropped and its content flows in reading order.', NULL),
  ('embed', '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0,
   'Raw LaTeX (TikZ, custom environments) has no EPUB form yet; phase 3 chooses between a picture made from the compile and a visible placeholder.', 'Today the block vanishes.'),
  ('footnote', '', 'render', 'aside', NULL, 'footnote', 'doc-footnote', NULL, 'note', '{""text"":""inline"",""number"":""a.backlink""}', 'none', 'none', NULL, 'div', 'endnote', 10, NULL,
   'Written at the end of its chapter file and linked from its noteref; EPUB 2: a linked endnote.'),
  ('algorithm', '', 'render', 'figure', 'pre', NULL, NULL, 'algorithm', 'algorithm', '{""lines"":""code"",""caption"":""figcaption"",""label"":""id""}', 'none', 'none', NULL, 'div', NULL, 10, NULL,
   'The lines are in the block''s code field, which today''s exporter never reads.'),
  ('callout', '', 'render', 'aside', NULL, NULL, 'note', 'callout', 'callout', '{""title"":""p.callout-title"",""kind"":""class"",""text"":""inline""}', 'none', 'none', NULL, 'div', NULL, 10, NULL,
   'Today the title and kind (warning, tip) are lost.'),
  ('personalInfo', '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'CV blocks make the PDF résumé; an e-book has no form for them (they are hidden from the editor''s menus).', NULL),
  ('photo',        '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'CV blocks make the PDF résumé; an e-book has no form for them (they are hidden from the editor''s menus).', NULL),
  ('cvEntry',      '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'CV blocks make the PDF résumé; an e-book has no form for them (they are hidden from the editor''s menus).', NULL),
  ('cvSection',    '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'CV blocks make the PDF résumé; an e-book has no form for them (they are hidden from the editor''s menus).', NULL),
  ('frontMatter', '', 'render', 'section', NULL, 'frontmatter', NULL, NULL, 'section-wrapper', NULL, 'starts-file', 'none', NULL, 'div', NULL, 10, NULL, NULL),
  ('backMatter',  '', 'render', 'section', NULL, 'backmatter',  NULL, NULL, 'section-wrapper', NULL, 'starts-file', 'none', NULL, 'div', NULL, 10, NULL, NULL),
  ('verse', '', 'render', 'p', NULL, NULL, NULL, 'verse', 'verse', '{""text"":""lines""}', 'none', 'none', NULL, 'p', NULL, 10, NULL, 'Each line ends with a br; the style sheet keeps the indentation.'),
  ('aside', '', 'render', 'aside', NULL, NULL, NULL, NULL, 'aside', '{""text"":""inline""}', 'none', 'none', NULL, 'div', NULL, 10, NULL, NULL),
  ('annotation', '', 'render', 'aside', NULL, NULL, 'note', 'annotation', 'aside', '{""text"":""inline""}', 'none', 'none', NULL, 'div', NULL, 10, NULL,
   'No epub:type: annotation is deprecated (EPUBCheck OPF-086b today), so the role says it.'),
  ('cover', '', 'render', 'section', 'img', 'cover', NULL, NULL, 'cover', '{""src"":""asset"",""alt"":""alt""}', 'starts-file', 'landmark', ARRAY['cover-image'], 'div', NULL, 10, NULL,
   'cover-image goes on the image''s manifest item and doc-cover on the img; EPUB 2: meta name=cover and a guide reference. Today the cover isn''t declared.'),
  ('chapterBreak', '', 'render', NULL, NULL, NULL, NULL, NULL, 'file-break', NULL, 'starts-file', 'none', NULL, NULL, NULL, 0, NULL, 'Starts a new file and writes nothing itself.'),
  ('inv-header',           '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-party',            '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-line-items',       '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-tax-summary',      '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-totals',           '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-payment',          '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-allowance-charge', '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-delivery',         '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('inv-note',             '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Invoice blocks make e-invoices (Factur-X), not e-books.', NULL),
  ('slide', '', 'unsupported', NULL, NULL, NULL, NULL, NULL, NULL, NULL, 'none', 'none', NULL, NULL, NULL, 0, 'Slides make presentations; an e-book has no form for them (hidden from the editor''s menus).', NULL),
  ('quote', '', 'render', 'blockquote', NULL, NULL, NULL, NULL, 'blockquote', '{""text"":""inline""}', 'none', 'none', NULL, 'blockquote', NULL, 0, NULL, 'Legacy name of blockquote, kept so old documents export.'),
  ('image', '', 'render', 'figure', 'img', NULL, NULL, NULL, 'figure', '{""src"":""asset"",""alt"":""alt"",""caption"":""figcaption""}', 'none', 'none', NULL, 'div', NULL, 0, NULL, 'Legacy name of figure (today the picture is lost).'),
  ('divider', '', 'render', 'hr', NULL, NULL, NULL, NULL, 'rule', NULL, 'none', 'none', NULL, 'hr', NULL, 0, NULL, 'Legacy divider: a thematic break.');

DO $$
DECLARE missing text;
BEGIN
  SELECT string_agg(DISTINCT b.block_type || '/' || b.variant || ': ' || x.kind || ' ' || x.name, '; ') INTO missing
  FROM epub_seed_block b
  CROSS JOIN LATERAL (VALUES ('xhtml-element', b.element), ('xhtml-element', b.inner_element), ('structural-semantic', b.epub_type),
                             ('aria-role', b.aria_role), ('xhtml-element', b.epub2_element)) AS x(kind, name)
  WHERE x.name IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM epub_constructs c WHERE c.kind = x.kind AND c.name = x.name);
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'EPUB block mappings name constructs the catalog lacks: %', missing;
  END IF;
END $$;

INSERT INTO epub_block_mappings (block_type, variant, disposition, element_id, inner_element_id, epub_type_id, aria_role_id,
  css_class, template_key, slots, file_split, nav, manifest_properties, epub2_element_id, epub2_template_key, import_priority,
  coverage_level, reason, notes)
SELECT b.block_type, b.variant, b.disposition, el.id, inner_el.id, et.id, ar.id,
       b.css_class, b.template_key, b.slots::jsonb, b.file_split, b.nav, COALESCE(b.manifest_properties, '{}'), e2.id, b.epub2_template_key,
       b.import_priority, CASE WHEN b.disposition = 'unsupported' THEN 'unsupported' ELSE 'none' END, b.reason, b.notes
FROM epub_seed_block b
LEFT JOIN epub_constructs el       ON el.kind = 'xhtml-element'       AND el.name = b.element
LEFT JOIN epub_constructs inner_el ON inner_el.kind = 'xhtml-element' AND inner_el.name = b.inner_element
LEFT JOIN epub_constructs et       ON et.kind = 'structural-semantic' AND et.name = b.epub_type
LEFT JOIN epub_constructs ar       ON ar.kind = 'aria-role'           AND ar.name = b.aria_role
LEFT JOIN epub_constructs e2       ON e2.kind = 'xhtml-element'       AND e2.name = b.epub2_element;

DROP TABLE epub_seed_block;
";

        // The inline marks Lilia's LML emitter writes (lml-specification.md, Inline Elements).
        private const string SeedInlineMappings = @"
DROP TABLE IF EXISTS epub_seed_inline;
CREATE TEMP TABLE epub_seed_inline (
  mark_key text, lml_syntax text, element text, epub_type text, aria_role text, attributes text, import_synonyms text[],
  manifest_properties text[], epub2_element text, epub2_form text, template_key text, notes text);

INSERT INTO epub_seed_inline VALUES
  ('bold',        '**text**',      'strong', NULL, NULL, NULL, ARRAY['b', 'span[style*=bold]'], NULL, 'strong', NULL, 'inline-mark', NULL),
  ('italic',      '*text*',        'em',     NULL, NULL, NULL, ARRAY['i', 'span[style*=italic]'], NULL, 'em', NULL, 'inline-mark', NULL),
  ('underline',   '__text__',      'u',      NULL, NULL, NULL, ARRAY['span[style*=underline]'], NULL, 'span', 'span class=underline (XHTML 1.1 has no u)', 'inline-mark', NULL),
  ('strike',      '~~text~~',      's',      NULL, NULL, NULL, ARRAY['del', 'strike', 'span[style*=line-through]'], NULL, 'del', 'del (XHTML 1.1 has no s)', 'inline-mark', NULL),
  ('highlight',   '==text==',      'mark',   NULL, NULL, NULL, ARRAY['span[style*=background]'], NULL, 'span', 'span class=highlight', 'inline-mark', NULL),
  ('superscript', '^text^',        'sup',    NULL, NULL, NULL, NULL, NULL, 'sup', NULL, 'inline-mark', NULL),
  ('subscript',   '%%text%%',      'sub',    NULL, NULL, NULL, NULL, NULL, 'sub', NULL, 'inline-mark', NULL),
  ('code',        '`code`',        'code',   NULL, NULL, NULL, ARRAY['kbd', 'samp', 'tt'], NULL, 'code', NULL, 'inline-mark', NULL),
  ('math',        '$math$, \(math\), \[math\]', 'math', NULL, NULL, 'alttext', NULL, ARRAY['mathml'], 'img', 'a PNG of the formula with alt text, or Unicode text', 'inline-math',
   'The import reads the LaTeX back from annotation encoding=application/x-tex when present. Today printed as source, and imported as garbled text.'),
  ('href',        '\href{url}{text}', 'a',   NULL, NULL, 'href', ARRAY['a[href]'], NULL, 'a', NULL, 'link', NULL),
  ('cite',        '@cite{key}',    'a',      NULL, 'doc-biblioref', 'href=#bib-{key}', ARRAY['a[role=doc-biblioref]', 'a[epub:type=biblioref]'], NULL, 'a', NULL, 'citation', 'Links to the bibliography entry; today printed as typed.'),
  ('xref',        '@ref{label}, Kind~(label)', 'a', NULL, NULL, 'href=#{label}', NULL, NULL, 'a', NULL, 'cross-reference', 'Prints the target''s number from the last compile.'),
  ('footnote',    '\footnote{text}', 'a',    'noteref', 'doc-noteref', 'href=#fn-{n}', ARRAY['a[epub:type=noteref]', 'a[role=doc-noteref]'], NULL, 'a', 'a link to an endnote', 'noteref',
   'The note body goes into an aside epub:type=footnote at the end of the chapter file.');

DO $$
DECLARE missing text;
BEGIN
  SELECT string_agg(DISTINCT i.mark_key || ': ' || x.kind || ' ' || x.name, '; ') INTO missing
  FROM epub_seed_inline i
  CROSS JOIN LATERAL (VALUES ('xhtml-element', i.element), ('structural-semantic', i.epub_type), ('aria-role', i.aria_role),
                             ('xhtml-element', i.epub2_element)) AS x(kind, name)
  WHERE x.name IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM epub_constructs c WHERE c.kind = x.kind AND c.name = x.name);
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'EPUB inline mappings name constructs the catalog lacks: %', missing;
  END IF;
END $$;

INSERT INTO epub_inline_mappings (mark_key, lml_syntax, direction, element_id, epub_type_id, aria_role_id, attributes, import_synonyms,
  manifest_properties, epub2_element_id, epub2_form, template_key, coverage_level, notes)
SELECT i.mark_key, i.lml_syntax, 'both', el.id, et.id, ar.id, i.attributes, COALESCE(i.import_synonyms, '{}'),
       COALESCE(i.manifest_properties, '{}'), e2.id, i.epub2_form, i.template_key, 'none', i.notes
FROM epub_seed_inline i
JOIN epub_constructs el      ON el.kind = 'xhtml-element'       AND el.name = i.element
LEFT JOIN epub_constructs et ON et.kind = 'structural-semantic' AND et.name = i.epub_type
LEFT JOIN epub_constructs ar ON ar.kind = 'aria-role'           AND ar.name = i.aria_role
LEFT JOIN epub_constructs e2 ON e2.kind = 'xhtml-element'       AND e2.name = i.epub2_element;

DROP TABLE epub_seed_inline;
";

        // What each target writes in its package: EPUB 3.4 (the default file), EPUB 2.0.1 (older readers) and
        // the EPUB Accessibility 1.2 discovery metadata.
        private const string SeedMetadataRules = @"
DROP TABLE IF EXISTS epub_seed_metadata;
CREATE TEMP TABLE epub_seed_metadata (kind text, name text, version_slug text, requirement text, source text, expression text, notes text);

INSERT INTO epub_seed_metadata VALUES
  ('package-metadata', 'dc:identifier',    'epub-3.4', 'required',    'document.epub_identifier', '<dc:identifier id=""pub-id"">urn:uuid:...</dc:identifier>', 'Made once per document and kept, so a re-export updates the same book.'),
  ('package-metadata', 'dc:title',         'epub-3.4', 'required',    'document.title',           NULL, NULL),
  ('package-metadata', 'dc:language',      'epub-3.4', 'required',    'document.language',        NULL, 'Also lang and dir on every page, and page-progression-direction=rtl for a right-to-left language.'),
  ('package-metadata', 'dcterms:modified', 'epub-3.4', 'required',    'computed:export_time',     '<meta property=""dcterms:modified"">CCYY-MM-DDThh:mm:ssZ</meta>', NULL),
  ('package-metadata', 'dc:creator',       'epub-3.4', 'recommended', 'document.authors',         '<dc:creator id=""creator-n""> refined by role aut and file-as', NULL),
  ('package-metadata', 'dc:date',          'epub-3.4', 'optional',    'document.published_on',    NULL, NULL),
  ('package-metadata', 'dc:publisher',     'epub-3.4', 'optional',    'document.publisher',       NULL, NULL),
  ('package-metadata', 'dc:description',   'epub-3.4', 'optional',    'document.abstract',        NULL, NULL),
  ('package-metadata', 'dc:subject',       'epub-3.4', 'optional',    'document.keywords',        NULL, NULL),
  ('package-metadata', 'dc:rights',        'epub-3.4', 'optional',    'document.rights',          NULL, NULL),

  ('package-metadata', 'dc:identifier',    'epub-2.0.1', 'required',    'document.epub_identifier', '<dc:identifier id=""pub-id"" opf:scheme=""uuid"">', 'The same identifier as the EPUB 3 file.'),
  ('package-metadata', 'dc:title',         'epub-2.0.1', 'required',    'document.title',           NULL, NULL),
  ('package-metadata', 'dc:language',      'epub-2.0.1', 'required',    'document.language',        NULL, 'Also xml:lang on every page.'),
  ('package-metadata', 'dc:creator',       'epub-2.0.1', 'recommended', 'document.authors',         '<dc:creator opf:role=""aut"" opf:file-as=""..."">', NULL),
  ('package-metadata', 'dc:date',          'epub-2.0.1', 'optional',    'computed:export_time',     '<dc:date opf:event=""modification"">', NULL),
  ('package-metadata', 'meta-name-cover',  'epub-2.0.1', 'optional',    'document.cover_asset',     '<meta name=""cover"" content=""cover-image""/>', 'Only when the document has a cover.'),

  ('a11y-property', 'schema:accessModeSufficient', 'epub-a11y-1.2', 'required',    'computed:access_mode_sufficient', NULL, 'textual when every image has alt text; textual,visual otherwise.'),
  ('a11y-property', 'schema:accessMode',           'epub-a11y-1.2', 'recommended', 'computed:access_modes',           NULL, 'textual, plus visual when the book has images.'),
  ('a11y-property', 'schema:accessibilityFeature', 'epub-a11y-1.2', 'recommended', 'computed:features',               NULL, 'structuralNavigation, tableOfContents, readingOrder, displayTransformability; alternativeText at 100% alt-text coverage; MathML when there is maths.'),
  ('a11y-property', 'schema:accessibilityHazard',  'epub-a11y-1.2', 'recommended', 'constant:none',                   NULL, NULL),
  ('a11y-property', 'schema:accessibilitySummary', 'epub-a11y-1.2', 'recommended', 'computed:summary',                NULL, 'Generated from the document; the author can override it.'),
  ('a11y-property', 'dcterms:conformsTo',          'epub-a11y-1.2', 'optional',    'computed:conformance',            NULL, 'Only after an accessibility check passes (DAISY Ace); never claimed by default.'),
  ('a11y-property', 'a11y:certifiedBy',            'epub-a11y-1.2', 'optional',    'document.certifier',              NULL, 'Only with a conformance claim.');

DO $$
DECLARE missing text;
BEGIN
  SELECT string_agg(m.kind || ' ' || m.name, '; ') INTO missing
  FROM epub_seed_metadata m
  WHERE NOT EXISTS (SELECT 1 FROM epub_constructs c WHERE c.kind = m.kind AND c.name = m.name);
  IF missing IS NOT NULL THEN
    RAISE EXCEPTION 'EPUB metadata rules name constructs the catalog lacks: %', missing;
  END IF;
END $$;

INSERT INTO epub_metadata_rules (construct_id, version_slug, requirement, source, expression, notes)
SELECT c.id, m.version_slug, m.requirement, m.source, m.expression, m.notes
FROM epub_seed_metadata m
JOIN epub_constructs c ON c.kind = m.kind AND c.name = m.name;

DROP TABLE epub_seed_metadata;
";

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "epub_block_mappings");

            migrationBuilder.DropTable(
                name: "epub_construct_support");

            migrationBuilder.DropTable(
                name: "epub_inline_mappings");

            migrationBuilder.DropTable(
                name: "epub_metadata_rules");

            migrationBuilder.DropTable(
                name: "epub_constructs");

            migrationBuilder.DropTable(
                name: "epub_spec_versions");
        }
    }
}
