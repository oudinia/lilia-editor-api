using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP hardening T4 (cloud session's handoff, 12 Oct): structure the campaign wrote that stopped
/// the compile or printed wrong. Asserted on the emitted LaTeX, then compiled for real.
/// </summary>
public class StructureBreakerTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Document Doc(string cls, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = cls,
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>(),
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) b.Document = doc;
        return doc;
    }

    private string Render(string type, object content) => _render.RenderBlockToLatex(B(type, content));

    private static Task<byte[]> Compile(string tex) =>
        new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);

    // ── MCP-68 ──────────────────────────────────────────────────────────
    [Fact]
    public void The_shims_give_a_book_an_abstract() =>
        LaTeXPreamble.JournalShims.Should().Contain(@"\@ifundefined{abstract}{\newenvironment{abstract}");

    // ── MCP-22 ──────────────────────────────────────────────────────────
    [Fact]
    public void An_algorithm_comment_is_a_line_of_its_own()
    {
        var latex = Render("algorithm", new
        {
            lines = new object[] { new { kind = "if", text = "$x > 0$" }, new { kind = "comment", text = "positive" }, new { kind = "statement", text = "y" }, new { kind = "endif", text = "" } },
        });
        latex.Should().Contain(@"\STATE \COMMENT{positive}");
    }

    // ── MCP-23 ──────────────────────────────────────────────────────────
    [Fact]
    public void Split_gets_a_host_and_a_bare_alignment_becomes_align()
    {
        Render("equation", new { latex = @"\begin{split} a &= b \\ &= c \end{split}", mode = "display" })
            .Should().StartWith(@"\begin{equation}").And.Contain(@"\begin{split}");
        Render("equation", new { latex = @"a &= b \\ c &= d", mode = "display", numbered = false })
            .Should().StartWith(@"\begin{align*}");
        Render("equation", new { latex = @"\begin{pmatrix} 1 & 0 \end{pmatrix}", mode = "display", numbered = false })
            .Should().StartWith(@"\begin{equation*}");
    }

    // ── MCP-24 ──────────────────────────────────────────────────────────
    [Fact]
    public void A_column_break_is_safe_in_one_column() =>
        Render("columnbreak", new { }).Should().Be(@"\liliaColumnBreak");

    // ── MCP-21 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData("simple")]
    [InlineData("verse")]
    public void A_quote_prints_its_attribution(string variant) =>
        Render("blockquote", new { text = "The engine weaves.", variant, attribution = "Ada Lovelace" })
            .Should().Contain(@"\raggedleft--- Ada Lovelace");

    // ── MCP-43 ──────────────────────────────────────────────────────────
    [Fact]
    public void Three_columns_go_through_multicol()
    {
        var doc = Doc("article", B("paragraph", new { text = "Body." }));
        doc.Columns = 3;
        var latex = _render.RenderToLatex(doc);
        latex.Should().Contain(@"\begin{multicols*}{3}").And.Contain(@"\usepackage{multicol}");
        LaTeXPreambleBuilder.BuildClassDirective(doc).Should().NotContain("twocolumn");
    }

    // ── MCP-51 ──────────────────────────────────────────────────────────
    [Fact]
    public void And_in_a_title_is_the_word() =>
        RenderService.TitleWithoutAnd(@"Logic \and Computation").Should().Be("Logic and Computation");

    // ── MCP-62 ──────────────────────────────────────────────────────────
    [Theory]
    [InlineData(0.95, @"0.95\linewidth")]
    [InlineData(95, @"0.95\linewidth")]
    [InlineData(1.5, @"1\linewidth")]
    public void A_figure_keeps_its_width_in_its_column(double width, string expected)
    {
        FigureWidth.Latex(JsonDocument.Parse(JsonSerializer.Serialize(new { width })).RootElement).Should().Be(expected);
        Render("figure", new { src = "a.png", width = 0.95 }).Should().Contain(@"width=0.95\linewidth");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_campaign_inputs_compile()
    {
        var book = Doc("book",
            B("abstract", new { text = "We study things." }),
            B("heading", new { text = "Start", level = 1 }),
            B("columnbreak", new { }),
            B("blockquote", new { text = "The engine weaves.", attribution = "Ada Lovelace" }),
            B("blockquote", new { text = "Line one,\nline two.", variant = "verse", attribution = "Anon" }),
            B("equation", new { latex = @"\begin{split} a &= b \\ &= c \end{split}", mode = "display" }),
            B("equation", new { latex = @"a &= b \\ c &= d", mode = "display" }),
            B("algorithm", new
            {
                lines = new object[] { new { kind = "if", text = "$x > 0$" }, new { kind = "comment", text = "positive" }, new { kind = "statement", text = "$y \\gets x$" }, new { kind = "endif", text = "" } },
            }));
        book.Title = @"Logic \and Computation";
        (await Compile(_render.RenderToLatex(book))).Should().NotBeEmpty();

        var three = Doc("article", B("paragraph", new { text = "One." }), B("columnbreak", new { }), B("paragraph", new { text = "Two." }));
        three.Columns = 3;
        (await Compile(_render.RenderToLatex(three))).Should().NotBeEmpty();
    }
}
