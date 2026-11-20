using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Shared.EpubCatalog;

/// <summary>
/// EPUB phase 1 (20 Nov 2026): the seed carries what the audit learned from the specifications and from
/// EPUBCheck 5.4.0 (lilia-docs/reports/2026-11-20-epub-export-audit.md). These tests pin those findings as data,
/// so the phase 3 writer can't be built on a catalog that forgot them.
/// </summary>
public class EpubCatalogSeedTests : IntegrationTestBase
{
    public EpubCatalogSeedTests(TestDatabaseFixture fixture) : base(fixture) { }

    private async Task<string?> StatusAsync(string kind, string name, string version)
    {
        await using var db = CreateDbContext();
        return await db.EpubConstructSupport.AsNoTracking()
            .Where(s => s.Construct!.Kind == kind && s.Construct.Name == name && s.VersionSlug == version)
            .Select(s => s.Status)
            .SingleOrDefaultAsync();
    }

    [Fact]
    public async Task The_versions_run_from_epub_2_0_1_to_3_4_and_the_targets_are_3_4_2_0_1_and_accessibility_1_2()
    {
        await using var db = CreateDbContext();
        var versions = await db.EpubSpecVersions.AsNoTracking().OrderBy(v => v.SortOrder).ToListAsync();

        versions.Select(v => v.Slug).Should().Equal("epub-2.0.1", "epub-3.0", "epub-3.3", "epub-3.4", "epub-a11y-1.1", "epub-a11y-1.2");
        versions.Where(v => v.IsExportTarget).Select(v => v.Slug).Should().BeEquivalentTo("epub-2.0.1", "epub-3.4", "epub-a11y-1.2");
        versions.Single(v => v.Slug == "epub-3.4").PackageVersion.Should().Be("3.0", "a 3.4 book still says version=3.0");
    }

    [Theory]
    [InlineData("structural-semantic", "annotation", "epub-3.4", "deprecated")]   // OPF-086b on today's annotation block
    [InlineData("aria-role", "doc-biblioentry", "epub-3.4", "deprecated")]        // RSC-017 in the prototypes
    [InlineData("nav-type", "ncx", "epub-3.3", "legacy")]
    [InlineData("nav-type", "ncx", "epub-3.4", "outdated")]                       // OBS-001 once NCX-001 is fixed
    [InlineData("nav-type", "ncx", "epub-2.0.1", "supported")]
    [InlineData("nav-type", "guide", "epub-3.4", "outdated")]
    [InlineData("media-type", "image/avif", "epub-3.4", "supported")]
    [InlineData("rendition-property", "rendition:align-x-center", "epub-3.4", "deprecated")]
    [InlineData("package-metadata", "pageBreakSource", "epub-3.4", "supported")]
    [InlineData("package-metadata", "opf:role", "epub-3.4", "removed")]
    [InlineData("a11y-property", "a11y:contactEmail", "epub-a11y-1.2", "supported")]
    public async Task The_audits_findings_hold_as_data(string kind, string name, string version, string expected)
    {
        (await StatusAsync(kind, name, version)).Should().Be(expected);
    }

    [Theory]
    [InlineData("xhtml-element", "math", "epub-2.0.1")]       // no MathML in EPUB 2
    [InlineData("xhtml-element", "section", "epub-2.0.1")]    // RSC-005 when relabelled as EPUB 2
    [InlineData("xhtml-element", "figure", "epub-2.0.1")]
    [InlineData("structural-semantic", "footnote", "epub-2.0.1")]
    [InlineData("media-type", "image/avif", "epub-3.3")]      // older readers don't show AVIF
    [InlineData("a11y-property", "a11y:contactEmail", "epub-a11y-1.1")]
    public async Task A_construct_a_version_lacks_has_no_row_for_it(string kind, string name, string version)
    {
        (await StatusAsync(kind, name, version)).Should().BeNull();
    }

    [Fact]
    public async Task The_mappings_carry_the_audits_design_rows()
    {
        await using var db = CreateDbContext();
        var rows = await db.EpubBlockMappings.AsNoTracking()
            .Select(b => new
            {
                b.BlockType, b.Variant, b.Disposition, b.FileSplit, b.Nav, b.ManifestProperties, b.Epub2TemplateKey,
                Element = b.Element!.Name, EpubType = b.EpubType!.Name, Role = b.AriaRole!.Name,
            })
            .ToListAsync();

        var chapter = rows.Single(r => r.BlockType == "heading" && r.Variant == "level-1");
        (chapter.Element, chapter.EpubType, chapter.Role, chapter.FileSplit, chapter.Nav)
            .Should().Be(("section", "chapter", "doc-chapter", "starts-file", "toc-entry"));

        var equation = rows.Single(r => r.BlockType == "equation" && r.Variant == "");
        equation.ManifestProperties.Should().Equal("mathml");
        equation.Epub2TemplateKey.Should().Be("equation-image");

        var annotation = rows.Single(r => r.BlockType == "annotation");
        annotation.EpubType.Should().BeNull("the deprecated epub:type annotation is never written");
        annotation.Role.Should().Be("note");

        rows.Single(r => r.BlockType == "columnBreak").Disposition.Should().Be("drop");
        rows.Single(r => r.BlockType == "inv-header").Disposition.Should().Be("unsupported");
    }
}
