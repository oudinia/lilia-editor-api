using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Mcp;
using Lilia.Api.Services;
using Lilia.Api.Tests.Features.Export.Publishers;
using Lilia.Core.Blocks;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static Lilia.Api.Tests.Features.AskLilia.AskLiliaHarness;

namespace Lilia.Api.Tests.Features.Connector;

/// <summary>
/// What Grok found writing a real ACM paper through the connector (launch-bot #122, 17 Nov 2026): the page count
/// (#125), two calls at once (#126), arXiv references (#127), Markdown headings after the title (#128), and the ACM
/// fields no tool could set (#129).
/// </summary>
public class GrokRetestFindingsTests : IDisposable
{
    private readonly string _memoDir = Path.Combine(Path.GetTempPath(), $"lilia-memos-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_memoDir)) Directory.Delete(_memoDir, true);
    }

    // ── #125: the page count is the exported PDF's ──────────────────────

    /// <summary>A PDF whose page tree is inside a compressed object stream, as pdflatex writes it.</summary>
    internal static byte[] PdfWithCompressedPageTree(int pages)
    {
        using var packed = new MemoryStream();
        using (var z = new ZLibStream(packed, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(Encoding.Latin1.GetBytes($"3 0 4 40 <</Type/Pages/Count {pages}/Kids[5 0 R]>> <</Type/Catalog/Pages 3 0 R>>"));
        var head = Encoding.Latin1.GetBytes($"%PDF-1.5\n1 0 obj\n<</Type/ObjStm/N 2/First 9/Length {packed.Length}/Filter/FlateDecode>>\nstream\n");
        var tail = Encoding.Latin1.GetBytes("\nendstream\nendobj\n%%EOF\n");
        return [.. head, .. packed.ToArray(), .. tail];
    }

    [Fact]
    public void The_page_count_is_read_inside_the_compressed_object_streams()
    {
        PdfPages.Count(PdfWithCompressedPageTree(2)).Should().Be(2);
        PdfPages.Count(Encoding.Latin1.GetBytes("%PDF-1.4\n1 0 obj\n<</Type /Pages /Count 5 /Kids [2 0 R]>>\nendobj\n%%EOF")).Should().Be(5);
        PdfPages.Count(Encoding.Latin1.GetBytes("%PDF-1.4\nnothing here\n%%EOF")).Should().BeNull();
    }

    private DocumentCheckService Checker(Lilia.Core.DTOs.DocumentDto doc, string source, IDocumentExportService exports)
    {
        var render = new Mock<IRenderService>();
        render.Setup(r => r.RenderToLatexAsync(doc.Id)).ReturnsAsync(source);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Mcp:CheckMemoDir"] = _memoDir }).Build();
        var memos = new CheckMemoStore(new Mock<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().Object, config, NullLogger<CheckMemoStore>.Instance);
        return new DocumentCheckService(render.Object, new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance), memos, config,
            NullLogger<DocumentCheckService>.Instance, exports: exports);
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_the_check_gives_the_exported_PDFs_page_count_or_says_it_counted_one_pass()
    {
        var block = Guid.NewGuid();
        var source = $"\\documentclass{{article}}\n\\begin{{document}}\n% block:{block}\nOne page of text.\n\\end{{document}}\n";

        var doc = Doc(Block(block));
        var exports = new Mock<IDocumentExportService>();
        exports.Setup(e => e.ExportToPdfWithEngineAsync(doc.Id, "auto")).ReturnsAsync((PdfWithCompressedPageTree(2), "pdflatex"));
        var report = JsonSerializer.SerializeToElement((await Checker(doc, source, exports.Object).CheckAsync(doc)).Report);
        report.GetProperty("compiles").GetBoolean().Should().BeTrue(report.ToString());
        report.GetProperty("pages").GetInt32().Should().Be(2, "the PDF export_document gives has 2, whatever one pass counted");
        report.GetProperty("pagesNote").ValueKind.Should().Be(JsonValueKind.Null);

        var other = Doc(Block(block));
        var failing = new Mock<IDocumentExportService>();
        failing.Setup(e => e.ExportToPdfWithEngineAsync(other.Id, "auto")).ThrowsAsync(new InvalidOperationException("no PDF"));
        var fallback = JsonSerializer.SerializeToElement((await Checker(other, source + "% other\n", failing.Object).CheckAsync(other)).Report);
        fallback.GetProperty("pages").GetInt32().Should().Be(1);
        fallback.GetProperty("pagesNote").GetString().Should().Contain("one LaTeX pass");
    }

    // ── #126: two calls at once ──────────────────────────────────────────

    [Fact]
    public void A_concurrency_failure_is_recognised_wherever_it_is_wrapped()
    {
        var concurrency = new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("expected 1 row, affected 0");
        LiliaMcpWriteTools.IsConcurrency(concurrency).Should().BeTrue();
        LiliaMcpWriteTools.IsConcurrency(new InvalidOperationException("tool failed", concurrency)).Should().BeTrue();
        LiliaMcpWriteTools.IsConcurrency(new InvalidOperationException("something else")).Should().BeFalse();
    }

    // ── #127: arXiv references ───────────────────────────────────────────

    [Theory]
    [InlineData("215-225", "215--225")]
    [InlineData("215\u2013225", "215--225")]
    [InlineData("215--225", "215--225")]
    [InlineData("e1234", "e1234")]
    public void A_page_range_takes_BibTeXs_en_dash(string given, string written)
    {
        BibliographyService.PageRange(given).Should().Be(written);
    }

    [Fact]
    public void An_arXiv_preprint_prints_its_number_in_Lilias_own_bibliography()
    {
        RenderService.ArxivOf(JsonSerializer.SerializeToElement(new { eprint = "1612.01514", archiveprefix = "arXiv", primaryclass = "cs.DC" }))
            .Should().Be("arXiv:1612.01514 [cs.DC]");
        RenderService.ArxivOf(JsonSerializer.SerializeToElement(new { arxivid = "1612.01514" })).Should().Be("arXiv:1612.01514", "entries added before keep working");
        RenderService.ArxivOf(JsonSerializer.SerializeToElement(new { eprint = "hep-th/9901001", archiveprefix = "other" })).Should().BeNull();
        BibliographyService.ToBibTex("misc", "jayanti2016", JsonSerializer.SerializeToElement(new { eprint = "1612.01514", archiveprefix = "arXiv" }))
            .Should().StartWith("@misc{jayanti2016,").And.Contain("archiveprefix = {arXiv}");
    }

    // ── #128: Markdown headings after the title ──────────────────────────

    [Fact]
    public async Task After_a_markdown_title_the_level_2_headings_are_the_sections()
    {
        const string md = "# LTX-155 Markdown title\n\nSome notes.\n\n## Section\n\nA paragraph.\n\n### Detail\n\nMore.";
        var pasted = PastedText.WithLeadingH1AsTitle(await PastedText.ParseAsync(md, "MARKDOWN", new LatexParser(), null!, true, (_, _) => { }), md);
        pasted.Title.Should().Be("LTX-155 Markdown title");
        var levels = pasted.Blocks.Where(b => b.Type == "heading")
            .Select(b => PastedText.ContentJson(b).RootElement.GetProperty("level").GetInt32()).ToList();
        levels.Should().Equal(1, 2);
    }

    // ── #129: what ACM asks for, through the connector ───────────────────

    [Fact]
    public void Authors_with_affiliations_are_written_as_the_title_editor_writes_them_and_ACM_reads_them()
    {
        var text = LiliaMcpWriteTools.AuthorText([
            new("Grok Tester", "Lilia Editor", "Paris", "France", "grok@example.org"),
            new("Ada Lovelace", Country: "UK"),
        ]);
        text.Should().Be(@"Grok Tester\\Lilia Editor\\Paris\\France\\grok@example.org \and Ada Lovelace\\UK");

        var (pre, _) = PublisherClasses.FrontMatter("acmart", "A paper", text, null, [], s => s, "A paper");
        pre.Should().Contain(@"\author{Grok Tester}")
            .And.Contain(@"\affiliation{\institution{Lilia Editor, Paris}\country{France}}")
            .And.Contain(@"\email{grok@example.org}")
            .And.Contain(@"\author{Ada Lovelace}");
    }

    [Fact]
    public void A_plain_author_on_several_lines_keeps_them_as_lines_under_the_name()
    {
        LiliaMcpWriteTools.AuthorLines("Grok Tester\nLilia Editor\r\nFrance\ngrok@example.org")
            .Should().Be(@"Grok Tester\\Lilia Editor\\France\\grok@example.org");
        LiliaMcpWriteTools.AuthorLines("Grok Tester").Should().Be("Grok Tester");
    }

    [Fact]
    public void A_TikZ_figures_alt_text_is_its_ACM_description()
    {
        var content = JsonSerializer.SerializeToElement(new { kind = "tikz", source = @"\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}", caption = "A line" });
        TikzFigure.ToLatex(content, "A line", "fig:line", "figure", "A diagonal line.").Should().Contain(@"\Description{A diagonal line.}");
        TikzFigure.ToLatex(content, "A line", "fig:line").Should().NotContain(@"\Description");

        var own = JsonSerializer.SerializeToElement(new { kind = "tikz", source = @"\Description{Mine.}\begin{tikzpicture}\end{tikzpicture}", caption = "A line" });
        var latex = TikzFigure.ToLatex(own, "A line", "", "figure", "Another.");
        latex.Split(@"\Description").Length.Should().Be(2, "the source's own description is the only one");
    }

    [Fact]
    public void In_an_ACM_paper_a_TikZ_figure_is_described_by_its_alt_or_its_own_Description()
    {
        var p = PublisherProfiles.Find("acm-sigconf-final")!;
        var doc = PublisherClassTests.Doc(p.DocumentClass, p.ClassOptions, "Ada Lovelace\\\\Analytical Engines\\\\UK\\\\ada@engines.uk", new[] { "LaTeX" });
        doc.PublisherProfile = p.Id;
        void Add(object content)
        {
            var b = PublisherClassTests.B("figure", content);
            b.SortOrder = doc.Blocks.Count; b.Document = doc; b.DocumentId = doc.Id;
            doc.Blocks.Add(b);
        }
        Add(new { kind = "tikz", source = @"\begin{tikzpicture}\draw (0,0)--(1,1);\end{tikzpicture}", caption = "With alt", alt = "A diagonal line." });
        Add(new { kind = "tikz", source = @"\Description{Two dots.}\begin{tikzpicture}\fill (0,0) circle (1pt);\end{tikzpicture}", caption = "Own" });
        Add(new { kind = "tikz", source = @"\begin{tikzpicture}\draw (0,0) circle (1);\end{tikzpicture}", caption = "None" });

        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, doc.BibliographyEntries.ToList(), new LaTeXExportOptions());
        tex.Should().Contain(@"\Description{A diagonal line.}");
        PublisherCompliance.Check(doc, blocks, tex).Findings.Where(f => f.Code == "acm_description")
            .Should().ContainSingle("only the figure with neither alt nor its own \\Description");
    }
}
