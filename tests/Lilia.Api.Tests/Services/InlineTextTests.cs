using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP hardening T4 (cloud session's handoff, 12 Oct): text that printed wrong — maths and marks
/// escaped in captions, theorem titles and table cells, prices read as maths, a {#id} kept in a
/// heading. Asserted on both emitters, then compiled for real.
/// </summary>
public class InlineTextTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private string Render(string type, object content) => _render.RenderBlockToLatex(B(type, content));

    private static Document Doc(params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        return doc;
    }

    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions());

    // ── MCP-16 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("It costs $5 and the deluxe costs $10.", new[] { @"\$5", @"\$10" })]
    [InlineData("Let $x$ be the price, say $5.", new[] { "$x$", @"\$5" })]
    [InlineData("Check $5 + 3 = 8$ here.", new[] { "$5 + 3 = 8$" })]
    [InlineData("Area $2x$ and $5$.", new[] { "$2x$", "$5$" })]
    public void A_price_is_not_maths(string text, string[] expected)
    {
        var latex = Render("paragraph", new { text });
        foreach (var e in expected) latex.Should().Contain(e);
        if (text.Contains("costs $5")) latex.Should().NotMatchRegex(@"(?<!\\)\$5 and");
        var exported = Export(Doc(B("paragraph", new { text })));
        foreach (var e in expected) exported.Should().Contain(e);
    }

    // ── MCP-25 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_markdown_heading_anchor_is_its_label()
    {
        var r = new LmlTextParser().Parse("# Markdown Heading {#sec:md}\n\nBody.");
        var json = JsonSerializer.Serialize(r.Blocks[0].Content);
        json.Should().Contain("sec:md").And.NotContain("{#");
        json.Should().Contain("\"Markdown Heading\"");
    }

    // ── MCP-14 / 60 ─────────────────────────────────────────────────────
    [Fact]
    public void Captions_and_theorem_titles_keep_maths_and_marks()
    {
        Render("figure", new { src = "a.png", caption = @"Growth of $x^2$, **fast**" })
            .Should().Contain(@"\caption{Growth of $x^2$, \textbf{fast}}");
        Render("table", new { caption = @"Error $\epsilon$ by \textbf{model}", headers = new[] { "A" }, rows = new[] { new[] { "1" } } })
            .Should().Contain(@"Error $\epsilon$ by \textbf{model}");
        Render("theorem", new { theoremType = "theorem", title = @"Completeness of $C[0,1]$", text = "The space is complete." })
            .Should().Contain(@"[{Completeness of $C[0,1]$}]");
        var exported = Export(Doc(B("theorem", new { theoremType = "theorem", title = @"Computing $\sqrt{2}$", text = "x" })));
        exported.Should().Contain(@"[{Computing $\sqrt{2}$}]");
    }

    // ── MCP-15 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("**Ours**", @"\textbf{Ours}")]
    [InlineData("*italic*", @"\textit{italic}")]
    [InlineData(@"\emph{key}", @"\emph{key}")]
    [InlineData(@"\(a+b\)", "$a+b$")]
    [InlineData("`x_1`", @"\texttt{x\_1}")]
    [InlineData("2*3*4", "2*3*4")]
    [InlineData("a ** b", "a ** b")]
    public void Marks_run_in_table_cells(string cell, string expected) =>
        LatexText.EscapeCell(cell).Should().Be(expected);

    [Fact]
    public void A_bold_header_is_bolded_once() =>
        LatexText.HeaderCell("**Ours**").Should().Be(@"\textbf{Ours}");

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_campaign_inputs_compile()
    {
        var doc = Doc(
            B("paragraph", new { text = "It costs $5 and the deluxe costs $10." }),
            B("theorem", new { theoremType = "theorem", title = @"Completeness of $C[0,1]$", text = "Complete." }),
            B("table", new { caption = @"Error $\epsilon$ by **model**", headers = new[] { "Model", "**Score**" }, rows = new[] { new[] { "**Ours**", @"\(0.9\)" }, new[] { "*Base*", @"\emph{0.8}" } } }),
            B("code", new { code = "x = 1", language = "python", caption = "Uses $x$" }));
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfAsync(_render.RenderToLatex(doc), "pdflatex", timeout: 180);
        pdf.Should().NotBeEmpty();
    }
}
