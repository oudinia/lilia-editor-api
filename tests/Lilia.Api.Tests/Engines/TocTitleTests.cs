using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Typst.TypstHarness;

namespace Lilia.Api.Tests.Engines;

/// <summary>
/// The document sweep (chunks 2 and 3, 16 Nov 2026; A-085, F-014): a table of contents' own title was ignored in every
/// format ("Contents" always), while the editor shows the block's title. A title the author set now prints; the stock
/// titles and an empty one keep each export's default, so a French document's LaTeX still says "Table des matières".
/// </summary>
public class TocTitleTests
{
    private static Block Toc(string json) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = "tableOfContents",
        Content = JsonDocument.Parse(json), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);

    [Theory]
    [InlineData("""{"title":"Overview of Chapters"}""", "Overview of Chapters")]
    [InlineData("""{"title":"  Plan  "}""", "Plan")]
    [InlineData("""{"title":"Table of Contents"}""", null)]
    [InlineData("""{"title":"table of contents"}""", null)]
    [InlineData("""{"title":"Contents"}""", null)]
    [InlineData("""{"title":""}""", null)]
    [InlineData("""{}""", null)]
    [InlineData("""{"title":3}""", null)]
    public void A_title_the_author_set_is_custom_and_the_stock_ones_are_not(string json, string? expected) =>
        TocTitle.Custom(JsonDocument.Parse(json).RootElement).Should().Be(expected);

    [Fact]
    public void LaTeX_prints_a_custom_title_before_the_contents()
    {
        var latex = Render.RenderBlockToLatex(Toc("""{"title":"Overview & Plan"}"""));
        latex.Should().Contain(@"\def\contentsname{Overview \& Plan}");
        // revtex4-2 (APS, AIP) prints \tocname instead (the sweep's chunk 5: U-280, U-304, U-328, U-352).
        latex.Should().Contain(@"\def\tocname{Overview \& Plan}");
        latex.IndexOf(@"\def", StringComparison.Ordinal).Should().BeLessThan(latex.IndexOf(@"\tableofcontents", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("""{"title":"Table of Contents"}""")]
    [InlineData("""{}""")]
    public void LaTeX_keeps_the_class_and_language_heading_for_a_stock_title(string json) =>
        Render.RenderBlockToLatex(Toc(json)).Trim().Should().Be(@"\tableofcontents");

    [Fact]
    public void The_Typst_preview_prints_a_custom_title()
    {
        var typst = new TypstRenderService(null!, NullLogger<TypstRenderService>.Instance);
        typst.RenderBlockToTypst(Toc("""{"title":"Overview"}""")).Should().Be("#outline(title: [Overview])");
        typst.RenderBlockToTypst(Toc("""{"title":"Table of Contents"}""")).Should().Be("#outline()");
    }

    [Fact]
    public void The_Typst_export_prints_a_custom_title()
    {
        var doc = new TypstExportService().BuildTypstDocument(Doc(), [Toc("""{"title":"Overview"}""")], null);
        doc.Should().Contain("#outline(title: [Overview])");
        new TypstExportService().BuildTypstDocument(Doc(), [Toc("""{"title":"Contents"}""")], null).Should().Contain("#outline()");
    }

    [Fact]
    public void HTML_and_Markdown_print_a_custom_title()
    {
        Render.RenderBlockToHtml(Toc("""{"title":"Overview <1>"}""")).Should().Contain("<h3>Overview &lt;1&gt;</h3>");
        Render.RenderBlockToHtml(Toc("""{}""")).Should().Contain("<h3>Table of Contents</h3>");
        Render.RenderBlockToMarkdown(Toc("""{"title":"Overview"}""")).Should().Be("## Overview");
        Render.RenderBlockToMarkdown(Toc("""{}""")).Should().Be("## Table of Contents");
    }

    [Theory]
    [InlineData("Overview", "Overview")]
    [InlineData(null, "Contents")]
    public async Task Word_prints_a_custom_title(string? title, string heading)
    {
        var block = new ExportBlock { Type = "tableOfContents", Content = new ExportBlockContent { Text = title } };
        var bytes = await new DocxExportService().ExportAsync(new ExportDocument { Title = "T", Blocks = [block] });

        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        var tocHeading = XDocument.Parse(reader.ReadToEnd()).Descendants(w + "p")
            .First(p => p.Descendants(w + "pStyle").Any(s => s.Attribute(w + "val")?.Value == "TOCHeading"));
        string.Concat(tocHeading.Descendants(w + "t").Select(t => t.Value)).Should().Be(heading);
    }
}
