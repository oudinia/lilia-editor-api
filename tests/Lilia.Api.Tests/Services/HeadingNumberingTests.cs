using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Document = Lilia.Core.Entities.Document;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Heading numbering is the author's choice (13 Oct, MCP-31): a document setting, the table of
/// contents follows it, and a DOCX import starts from what the Word file did.
/// </summary>
public class HeadingNumberingTests
{
    private static Document Doc(string cls, int? numbering) => new()
    {
        Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls, HeadingNumbering = numbering, Blocks = new List<Block>(), BibliographyEntries = new List<BibliographyEntry>(),
    };

    [Theory]
    [InlineData("article", 2, 2)]
    [InlineData("article", 1, 1)]
    [InlineData("book", 2, 1)]
    [InlineData("report", 3, 2)]
    public void Numbered_levels_set_secnumdepth_and_the_toc_follows(string cls, int levels, int depth)
    {
        var preamble = LaTeXPreambleBuilder.BuildLayoutPreamble(Doc(cls, levels));
        preamble.Should().Contain($"\\setcounter{{secnumdepth}}{{{depth}}}").And.Contain($"\\setcounter{{tocdepth}}{{{depth}}}");
    }

    [Fact]
    public void None_numbers_nothing_and_default_leaves_the_class_alone()
    {
        LaTeXPreambleBuilder.BuildLayoutPreamble(Doc("article", 0)).Should().Contain(@"\setcounter{secnumdepth}{-2}").And.NotContain("tocdepth");
        LaTeXPreambleBuilder.BuildLayoutPreamble(Doc("article", null)).Should().NotContain("secnumdepth");
    }

    [Theory]
    [InlineData("none", 0)]
    [InlineData("3", 3)]
    [InlineData("default", -1)]
    public void The_setting_reads_none_levels_and_default(string value, int stored) =>
        DocumentSettingsValidator.Validate(new DocumentSettingsValidator.Input(HeadingNumbering: value)).Update!.HeadingNumbering.Should().Be(stored);

    [Fact]
    public void A_wrong_value_is_refused() =>
        DocumentSettingsValidator.Validate(new DocumentSettingsValidator.Input(HeadingNumbering: "7")).Errors.Should().ContainSingle(e => e.Contains("headingNumbering"));

    [Fact]
    public void Numbered_headings_go_to_latex_not_typst() =>
        PageSetupRouting.WhyLatex(Doc("article", 2)).Should().Contain("heading numbering");

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task A_numbered_book_compiles()
    {
        var doc = Doc("book", 2);
        doc.Blocks = new List<Block>
        {
            new() { Id = Guid.NewGuid(), Type = "tableOfContents", Content = System.Text.Json.JsonDocument.Parse("{}") },
            new() { Id = Guid.NewGuid(), Type = "heading", Content = System.Text.Json.JsonDocument.Parse("""{"text":"Start","level":1}""") },
            new() { Id = Guid.NewGuid(), Type = "heading", Content = System.Text.Json.JsonDocument.Parse("""{"text":"Part","level":2}""") },
            new() { Id = Guid.NewGuid(), Type = "heading", Content = System.Text.Json.JsonDocument.Parse("""{"text":"Detail","level":3}""") },
        };
        var tex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        (await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180)).Should().NotBeEmpty();
    }

    // ── DOCX import ─────────────────────────────────────────────────────
    private static string Docx(bool styleNumbering, params (int Level, string Text)[] headings)
    {
        var path = Path.Combine(Path.GetTempPath(), $"hn-{Guid.NewGuid()}.docx");
        using var word = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document);
        var main = word.AddMainDocumentPart();
        var styles = main.AddNewPart<StyleDefinitionsPart>();
        styles.Styles = new Styles();
        foreach (var level in new[] { 1, 2 })
        {
            var ppr = new StyleParagraphProperties(new OutlineLevel { Val = level - 1 });
            if (styleNumbering) ppr.Append(new NumberingProperties(new NumberingLevelReference { Val = level - 1 }, new NumberingId { Val = 1 }));
            styles.Styles.Append(new Style(new StyleName { Val = $"heading {level}" }, ppr) { Type = StyleValues.Paragraph, StyleId = $"Heading{level}" });
        }
        if (styleNumbering)
        {
            var numbering = main.AddNewPart<NumberingDefinitionsPart>();
            numbering.Numbering = new Numbering(
                new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 }) { AbstractNumberId = 0 },
                new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 });
        }
        var body = new Body();
        foreach (var (level, text) in headings)
        {
            body.Append(new Paragraph(new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{level}" }), new Run(new Text(text))));
            body.Append(new Paragraph(new Run(new Text("Some body text."))));
        }
        main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
        main.Document.Save();
        return path;
    }

    [Fact]
    public async Task Word_heading_numbering_carries_over()
    {
        var doc = await new DocxParser().ParseAsync(Docx(true, (1, "Introduction"), (2, "Scope"), (1, "Method"), (2, "Data")));
        doc.Metadata.HeadingNumbering.Should().Be(2);
    }

    [Fact]
    public async Task Unnumbered_word_headings_import_as_none()
    {
        var doc = await new DocxParser().ParseAsync(Docx(false, (1, "Introduction"), (1, "Method")));
        doc.Metadata.HeadingNumbering.Should().Be(0);
    }

    [Fact]
    public async Task Typed_numbers_count_as_numbering()
    {
        var doc = await new DocxParser().ParseAsync(Docx(false, (1, "1. Introduction"), (2, "1.1 Scope"), (1, "2. Method")));
        doc.Metadata.HeadingNumbering.Should().Be(2);
    }
}
