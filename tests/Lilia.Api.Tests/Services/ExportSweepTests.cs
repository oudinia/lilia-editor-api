using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The export sweep of 13 Oct: every LTX- test document exported through the connector and its
/// .tex compiled. 9 of 36 failed; each case here is one document's input, on both emitters.
/// </summary>
public class ExportSweepTests
{
    private readonly RenderService _render = new(null!, NullLogger<RenderService>.Instance);

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    private static Document Doc(string? customPreamble, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "T", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article", CustomPreamble = customPreamble,
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

    private static Task<byte[]> Compile(string tex) =>
        new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfAsync(tex, EngineDetector.Detect(tex).ToCli(), timeout: 180);

    private static int Count(string s, string what) => (s.Length - s.Replace(what, "").Length) / what.Length;

    // LTX-50, 53, 54, 55, 56: the source's own \label, and the block's label beside it.
    [Fact]
    public void An_equation_with_its_own_label_gets_one()
    {
        var doc = Doc(null, B("equation", new { latex = @"\det(I - zB) = 0 \label{eq:ihara}", label = "eq:ihara", mode = "display" }));
        Count(_render.RenderToLatex(doc), @"\label{eq:ihara}").Should().Be(1);
        Count(Export(doc), @"\label{eq:ihara}").Should().Be(1);
    }

    // LTX-43: an empty equation.
    [Fact]
    public void An_empty_equation_is_left_out()
    {
        var doc = Doc(null, B("equation", new { latex = "", mode = "display" }));
        Export(doc).Should().Contain("% Empty equation").And.NotContain(@"\begin{equation}" + "\n\n");
    }

    // LTX-51, 52: a decimal at the start of a formula is not a price, and nothing leaks.
    [Theory]
    [InlineData(@"at $0.8\sigma$. The dominant", @"$0.8\sigma$")]
    [InlineData(@"by up to $3.1\times$ on graphs with $|E| \ge 10^8$ edges; it is $1.4$--$2.2\times$ faster.", @"$1.4$--$2.2\times$")]
    [InlineData("It costs $5, or $10.", @"\$10.")]
    public void A_decimal_in_maths_is_not_a_price(string text, string expected)
    {
        var doc = Doc(null, B("paragraph", new { text }));
        foreach (var latex in new[] { _render.RenderToLatex(doc), Export(doc) })
        {
            latex.Should().Contain(expected);
            latex.Should().NotContain("\x00").And.NotContain("PH1").And.NotContain("MATH");
        }
    }

    // LTX-34: a document written with pdflatex's CJK environment.
    private static Document CjkDoc() => Doc(@"\usepackage{CJKutf8}",
        B("paragraph", new { text = @"CJK: \begin{CJK}{UTF8}{gbsn}数学\end{CJK} and \begin{CJK}{UTF8}{min}日本語\end{CJK}." }));

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task The_sweeps_failures_compile_on_both_emitters()
    {
        var doc = Doc(null,
            B("equation", new { latex = @"\det(I - zB) = 0 \label{eq:ihara}", label = "eq:ihara", mode = "display" }),
            B("equation", new { latex = "", mode = "display" }),
            B("paragraph", new { text = @"By \eqref{eq:ihara}, up to $3.1\times$ at $0.8\sigma$; it is $1.4$--$2.2\times$ faster and costs $5, or $10." }));
        (await Compile(_render.RenderToLatex(doc))).Should().NotBeEmpty();
        (await Compile(Export(doc))).Should().NotBeEmpty();
        (await Compile(_render.RenderToLatex(CjkDoc()))).Should().NotBeEmpty();
        (await Compile(Export(CjkDoc()))).Should().NotBeEmpty();
    }

    // LTX-18: a listing labels itself in its options; a \ref to it is not dangling.
    [Fact]
    public void A_listing_label_counts_as_a_label()
    {
        var doc = Doc(null, B("paragraph", new { text = @"See \ref{lst:py}." }),
            B("code", new { code = "x = 1", language = "python", caption = "C", label = "lst:py" }));
        new EmittedLatexAudit(_render.RenderToLatex(doc)).DanglingReferences.Should().BeEmpty();
    }

    // LTX-11: a heading stored with its Markdown anchor still in the text; and the LML id on export.
    [Fact]
    public void A_heading_anchor_is_its_label_on_both_emitters()
    {
        var doc = Doc(null, B("heading", new { text = "Markdown Heading {#sec:md}", level = 1 }),
            B("heading", new { text = "By id", level = 2, id = "sec:byid" }));
        foreach (var latex in new[] { _render.RenderToLatex(doc), Export(doc) })
        {
            latex.Should().Contain(@"\section{Markdown Heading}\label{sec:md}").And.NotContain("{\\#");
            latex.Should().Contain(@"\label{sec:byid}");
        }
    }

    // LTX-34: the warning check_document reads to say characters are missing from the PDF.
    [Fact]
    public void The_dropped_characters_warning_carries_its_marker() =>
        LaTeXGlyphScanner.Describe(LaTeXGlyphScanner.Scan("Missing character: There is no П (U+041F) in font lmroman10-regular!"))
            .Should().Contain(LaTeXGlyphScanner.DroppedMarker);

    // MCP-84: an embed written with {"latex": …} prints, as one with {"code": …} does.
    [Fact]
    public void An_embed_takes_latex_as_well_as_code() =>
        _render.RenderBlockToLatex(B("embed", new { latex = @"\begin{center}Raw\end{center}" }))
            .Should().Contain(@"\begin{center}Raw\end{center}");

    // ── Run 2, R2 ───────────────────────────────────────────────────────
    [Fact]
    public void A_footnote_block_attaches_to_the_paragraph_before_it()
    {
        var tex = _render.RenderToLatex(Doc(null, B("paragraph", new { text = "A claim." }), B("footnote", new { text = "The source." })));
        tex.Should().Contain(@"A claim.\footnote{The source.");
    }

    [Fact]
    public void A_bracketed_description_term_keeps_its_label() =>
        _render.RenderBlockToLatex(B("list", new { kind = "description", items = new[] { new { text = "[Bracket] term", description = "d" } } }))
            .Should().Contain(@"\item[{[Bracket] term}]");

    [Fact]
    public void Single_quotes_open_and_close() =>
        _render.RenderBlockToLatex(B("paragraph", new { text = "She said 'hello' and don't." }))
            .Should().Contain("`hello'").And.Contain("don't");

    [Fact]
    public void New_theorem_kinds_print_as_themselves()
    {
        _render.RenderBlockToLatex(B("theorem", new { theoremType = "exercise", text = "Do it." })).Should().Contain(@"\begin{exercise}");
        LaTeXPreamble.CustomPreambleForEmit(@"\newtheorem{exercise}{Exercise}").Should().Contain("left out");
        var r = new Lilia.Import.Services.LmlTextParser().Parse("@theorem[kind=claim]\n  It holds.");
        System.Text.Json.JsonSerializer.Serialize(r.Blocks[0].Content).Should().Contain("claim");
    }

    [Fact]
    public void A_wide_table_spans_both_columns() =>
        _render.RenderBlockToLatex(B("table", new { headers = new[] { "A" }, rows = new[] { new[] { "1" } }, span = "page" }))
            .Should().Contain(@"\begin{table*}").And.Contain(@"\end{table*}");

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Header_footer_and_numbering_compile()
    {
        var doc = Doc(null, B("paragraph", new { text = "Body." }), B("footnote", new { text = "Note." }));
        doc.HeaderLeft = "Left"; doc.FooterCenter = "Centre"; doc.PageNumbering = "Roman";
        var tex = _render.RenderToLatex(doc);
        tex.Should().Contain(@"\pagenumbering{Roman}").And.Contain(@"\rfoot{\thepage}").And.Contain(@"\setlength{\headheight}");
        (await Compile(tex)).Should().NotBeEmpty();
        doc.PageNumbering = "none";
        (await Compile(_render.RenderToLatex(doc))).Should().NotBeEmpty();
    }

    // LTX-85 (run 2 re-test): \ce{…} in table cells printed as text, and mhchem only came from a custom preamble.
    [Theory]
    [InlineData(@"\ce{C7H6O3}", @"\ce{C7H6O3}")]
    [InlineData(@"\SI{3}{\metre}", @"\SI{3}{\metre}")]
    [InlineData(@"\ce{\input{x}}", @"\textbackslash{}")]
    public void Chemistry_and_units_pass_in_cells(string cell, string expected) =>
        LatexText.EscapeCell(cell).Should().Contain(expected);

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task Chemistry_compiles_without_a_custom_preamble()
    {
        var doc = Doc(null,
            B("paragraph", new { text = @"Salicylic acid is \ce{C7H6O3}." }),
            B("table", new { headers = new[] { "Compound", "Mass" }, rows = new[] { new[] { @"\ce{C7H6O3}", @"\SI{138.1}{\gram\per\mole}" } } }));
        var tex = _render.RenderToLatex(doc);
        tex.Should().Contain("{mhchem}");
        (await Compile(tex)).Should().NotBeEmpty();
        (await Compile(Export(doc))).Should().NotBeEmpty();
    }
}
