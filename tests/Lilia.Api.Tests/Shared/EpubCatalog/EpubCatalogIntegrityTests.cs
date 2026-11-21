using System.Reflection;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Core.Entities.Epub;
using Microsoft.EntityFrameworkCore;

namespace Lilia.Api.Tests.Shared.EpubCatalog;

/// <summary>
/// EPUB phase 1 (20 Nov 2026): the EPUB catalog is held to the code from its first day, which the LaTeX catalog
/// wasn't (its parser still dispatches from hard-coded sets and the two drift). Today's EpubService has its own
/// switch: a block type it forgot printed an empty paragraph, and every file failed EPUBCheck. These tests run
/// on the migrated database and refuse a catalog where a block type has no row, a row names a writer nobody will
/// register, or a row would write something its target version doesn't have or has deprecated
/// (lilia-docs/reports/2026-11-20-epub-export-audit.md, "Integrity tests").
///
/// If one fails, fix the catalog row (or add the missing one). Never widen <see cref="BlockTemplates"/> to make
/// a red test green: each key is a writer phase 3 must register.
/// </summary>
public class EpubCatalogIntegrityTests : IntegrationTestBase
{
    public EpubCatalogIntegrityTests(TestDatabaseFixture fixture) : base(fixture) { }

    private const string Epub2 = "epub-2.0.1";
    private const string Epub33 = "epub-3.3";
    private const string Epub34 = "epub-3.4";
    private const string A11y12 = "epub-a11y-1.2";

    /// <summary>The block writers phase 3 registers, one per key.</summary>
    private static readonly HashSet<string> BlockTemplates = new(StringComparer.Ordinal)
    {
        "heading", "paragraph", "equation", "figure", "table", "code", "list", "blockquote", "verse", "theorem",
        "abstract", "title-page", "bibliography", "toc", "page-marker", "note", "algorithm", "callout",
        "section-wrapper", "aside", "cover", "file-break", "rule",
    };

    /// <summary>EPUB 2 writers that replace the EPUB 3 one: no MathML, no pop-up notes, or nothing at all.</summary>
    private static readonly HashSet<string> Epub2Templates = new(StringComparer.Ordinal) { "equation-image", "endnote", "drop" };

    /// <summary>The inline writers phase 3 registers.</summary>
    private static readonly HashSet<string> InlineTemplates = new(StringComparer.Ordinal)
    {
        "inline-mark", "inline-math", "link", "citation", "cross-reference", "noteref",
    };

    /// <summary>Every string constant on <see cref="BlockTypes"/>: the 40 in All, slide, and the legacy names
    /// old documents still hold. A new constant without a catalog row fails here.</summary>
    private static List<string> BlockTypeConstants() =>
        typeof(BlockTypes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    private sealed record Catalog(
        Dictionary<Guid, EpubConstruct> Constructs,
        Dictionary<(Guid, string), string> Support,
        List<EpubBlockMapping> Blocks,
        List<EpubInlineMapping> Inlines,
        List<EpubMetadataRule> Rules)
    {
        public string? StatusOf(Guid? id, string version) =>
            id is { } g && Support.TryGetValue((g, version), out var s) ? s : null;

        public string Describe(Guid? id) =>
            id is { } g && Constructs.TryGetValue(g, out var c) ? $"{c.Kind} {c.Name}" : "(none)";

        public EpubConstruct? Find(string kind, string name) =>
            Constructs.Values.FirstOrDefault(c => c.Kind == kind && c.Name == name);
    }

    private async Task<Catalog> LoadAsync()
    {
        await using var db = CreateDbContext();
        var constructs = await db.EpubConstructs.AsNoTracking().ToDictionaryAsync(c => c.Id);
        var support = await db.EpubConstructSupport.AsNoTracking()
            .ToDictionaryAsync(s => (s.ConstructId, s.VersionSlug), s => s.Status);
        var blocks = await db.EpubBlockMappings.AsNoTracking().ToListAsync();
        var inlines = await db.EpubInlineMappings.AsNoTracking().ToListAsync();
        var rules = await db.EpubMetadataRules.AsNoTracking().ToListAsync();
        return new Catalog(constructs, support, blocks, inlines, rules);
    }

    [Fact]
    public async Task Every_block_type_has_a_mapping_row()
    {
        var catalog = await LoadAsync();
        var mapped = catalog.Blocks.Where(b => b.Variant == "").Select(b => b.BlockType).ToHashSet(StringComparer.Ordinal);

        var missing = BlockTypeConstants().Where(t => !mapped.Contains(t)).ToList();

        missing.Should().BeEmpty("every BlockTypes constant needs a default EPUB row, even if it is an explicit drop or " +
            "unsupported with a reason; missing: {0}", string.Join(", ", missing));
        BlockTypes.All.Should().HaveCount(40, "the audit and the seed cover the 40 types in BlockTypes.All");
    }

    [Fact]
    public async Task No_mapping_row_names_a_block_type_Lilia_doesnt_have()
    {
        var catalog = await LoadAsync();
        var known = BlockTypeConstants().ToHashSet(StringComparer.Ordinal);

        catalog.Blocks.Where(b => !known.Contains(b.BlockType)).Select(b => b.BlockType)
            .Should().BeEmpty("a row for a block type that doesn't exist is a typo or a removed type");
    }

    [Fact]
    public async Task A_rendered_row_names_a_known_writer_and_a_dropped_or_unsupported_row_says_why()
    {
        var catalog = await LoadAsync();

        var badRender = catalog.Blocks
            .Where(b => b.Disposition == "render" && (b.TemplateKey is null || !BlockTemplates.Contains(b.TemplateKey)))
            .Select(b => $"{b.BlockType}/{b.Variant} → {b.TemplateKey ?? "(none)"}");
        badRender.Should().BeEmpty();

        var badEpub2 = catalog.Blocks
            .Where(b => b.Epub2TemplateKey is not null && !Epub2Templates.Contains(b.Epub2TemplateKey))
            .Select(b => $"{b.BlockType}/{b.Variant} → {b.Epub2TemplateKey}");
        badEpub2.Should().BeEmpty();

        var silent = catalog.Blocks
            .Where(b => b.Disposition != "render" && (string.IsNullOrWhiteSpace(b.Reason) || b.TemplateKey is not null))
            .Select(b => $"{b.BlockType}/{b.Variant}");
        silent.Should().BeEmpty("a block with no EPUB form says why, and names no writer");

        var noElement = catalog.Blocks
            .Where(b => b.Disposition == "render" && b.ElementId is null && b.TemplateKey != "file-break")
            .Select(b => $"{b.BlockType}/{b.Variant}");
        noElement.Should().BeEmpty("only a file break writes no element");
    }

    [Fact]
    public async Task Every_construct_a_rendered_block_writes_is_supported_in_epub_3_4_and_3_3()
    {
        var catalog = await LoadAsync();
        var problems = new List<string>();

        foreach (var b in catalog.Blocks.Where(b => b.Disposition == "render"))
        {
            var written = new List<(Guid? Id, string Expected)>
            {
                (b.ElementId, "xhtml-element"), (b.InnerElementId, "xhtml-element"),
                (b.EpubTypeId, "structural-semantic"), (b.AriaRoleId, "aria-role"),
            };
            written.AddRange(b.ManifestProperties.Select(p => (catalog.Find("manifest-property", p)?.Id, "manifest-property")));

            foreach (var (id, expected) in written)
            {
                if (id is null && expected != "manifest-property") continue;
                var kind = id is { } g && catalog.Constructs.TryGetValue(g, out var c) ? c.Kind : null;
                if (kind != expected)
                {
                    problems.Add($"{b.BlockType}/{b.Variant}: {catalog.Describe(id)} isn't a known {expected}");
                    continue;
                }
                foreach (var version in new[] { Epub34, Epub33 })
                    if (catalog.StatusOf(id, version) != "supported")
                        problems.Add($"{b.BlockType}/{b.Variant}: {catalog.Describe(id)} is {catalog.StatusOf(id, version) ?? "absent"} in {version}");
            }
        }

        problems.Should().BeEmpty("the default file targets EPUB 3.4 without breaking 3.3 readers: no row may write " +
            "a construct those versions lack, have deprecated or call outdated");
    }

    [Fact]
    public async Task Every_element_a_rendered_block_writes_for_older_readers_exists_in_epub_2()
    {
        var catalog = await LoadAsync();
        var problems = new List<string>();

        foreach (var b in catalog.Blocks.Where(b => b.Disposition == "render" && b.Epub2TemplateKey != "drop" && b.ElementId is not null))
        {
            // The outer element, or its EPUB 2 replacement (section → div).
            var outer = b.Epub2ElementId ?? b.ElementId;
            if (catalog.StatusOf(outer, Epub2) != "supported")
                problems.Add($"{b.BlockType}/{b.Variant}: {catalog.Describe(outer)} isn't in EPUB 2.0.1; give the row an epub2 element");

            // An inner element EPUB 2 lacks (math) needs an EPUB 2 writer that replaces it (equation-image).
            if (b.InnerElementId is not null && b.Epub2TemplateKey is null && catalog.StatusOf(b.InnerElementId, Epub2) != "supported")
                problems.Add($"{b.BlockType}/{b.Variant}: inner {catalog.Describe(b.InnerElementId)} isn't in EPUB 2.0.1 and no EPUB 2 writer replaces it");

            if (b.EpubTypeId is not null && b.Epub2ElementId is null)
                problems.Add($"{b.BlockType}/{b.Variant}: writes an epub:type but no EPUB 2 element (EPUB 2 has no epub:type)");
        }

        problems.Should().BeEmpty("the older-readers file is XHTML 1.1: section, figure, aside and MathML don't exist there " +
            "(EPUBCheck RSC-005 on today's files relabelled as EPUB 2)");
    }

    [Fact]
    public async Task Every_inline_mark_writes_what_epub_3_4_has_and_has_an_epub_2_form()
    {
        var catalog = await LoadAsync();
        var problems = new List<string>();

        foreach (var m in catalog.Inlines)
        {
            if (m.TemplateKey is null || !InlineTemplates.Contains(m.TemplateKey))
                problems.Add($"{m.MarkKey}: writer {m.TemplateKey ?? "(none)"} isn't a known inline writer");

            var written = new List<Guid?> { m.ElementId, m.EpubTypeId, m.AriaRoleId };
            written.AddRange(m.ManifestProperties.Select(p => catalog.Find("manifest-property", p)?.Id));
            foreach (var id in written.Where(id => id is not null))
                foreach (var version in new[] { Epub34, Epub33 })
                    if (catalog.StatusOf(id, version) != "supported")
                        problems.Add($"{m.MarkKey}: {catalog.Describe(id)} is {catalog.StatusOf(id, version) ?? "absent"} in {version}");
            if (m.ManifestProperties.Any(p => catalog.Find("manifest-property", p) is null))
                problems.Add($"{m.MarkKey}: names a manifest property the catalog lacks");

            var epub2 = m.Epub2ElementId ?? m.ElementId;
            if (catalog.StatusOf(epub2, Epub2) != "supported")
                problems.Add($"{m.MarkKey}: {catalog.Describe(epub2)} isn't in EPUB 2.0.1; give the mark an epub2 element");
        }

        problems.Should().BeEmpty();
        catalog.Inlines.Select(m => m.MarkKey).Should().Contain(new[]
        {
            "bold", "italic", "underline", "strike", "highlight", "superscript", "subscript", "code", "math", "href", "cite", "xref",
        }, "every inline mark Lilia's LML emitter writes needs a row (lml-specification.md, Inline Elements)");
    }

    [Fact]
    public async Task Every_metadata_rule_writes_a_field_its_target_version_supports()
    {
        var catalog = await LoadAsync();
        var problems = new List<string>();

        foreach (var r in catalog.Rules)
        {
            if (catalog.StatusOf(r.ConstructId, r.VersionSlug) != "supported")
                problems.Add($"{catalog.Describe(r.ConstructId)} for {r.VersionSlug}: {catalog.StatusOf(r.ConstructId, r.VersionSlug) ?? "absent"}");
            // Accessibility metadata is written into the EPUB 3 package, so 3.4 must have it too.
            if (r.VersionSlug == A11y12 && catalog.StatusOf(r.ConstructId, Epub34) != "supported")
                problems.Add($"{catalog.Describe(r.ConstructId)}: an accessibility rule the EPUB 3.4 package doesn't have");
        }

        problems.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Epub34, "dc:identifier,dc:title,dc:language,dcterms:modified")]
    [InlineData(Epub2, "dc:identifier,dc:title,dc:language")]
    [InlineData(A11y12, "schema:accessModeSufficient")]
    public async Task Each_target_writes_the_metadata_its_specification_requires(string version, string required)
    {
        var catalog = await LoadAsync();
        var requiredHere = catalog.Rules
            .Where(r => r.VersionSlug == version && r.Requirement == "required")
            .Select(r => catalog.Constructs[r.ConstructId].Name);

        requiredHere.Should().BeEquivalentTo(required.Split(','));
    }

    [Fact]
    public async Task Every_construct_exists_in_some_version_and_every_paired_role_is_a_current_one()
    {
        var catalog = await LoadAsync();

        var orphans = catalog.Constructs.Values.Where(c => !catalog.Support.Keys.Any(k => k.Item1 == c.Id))
            .Select(c => $"{c.Kind} {c.Name}");
        orphans.Should().BeEmpty("a construct with no version row says nothing about where it can be written");

        var badPairs = catalog.Constructs.Values.Where(c => c.PairedAriaRole is not null)
            .Where(c => catalog.Find("aria-role", c.PairedAriaRole!) is not { } role || catalog.StatusOf(role.Id, Epub34) != "supported")
            .Select(c => $"{c.Name} → {c.PairedAriaRole}");
        badPairs.Should().BeEmpty("an epub:type's paired role must exist and not be deprecated (doc-biblioentry is, RSC-017)");
    }
}
