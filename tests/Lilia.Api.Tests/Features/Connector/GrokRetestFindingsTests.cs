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
        var report = JsonSerializer.SerializeToElement((await Checker(doc, source, exports.Object).CheckWithPagesAsync(doc)).Report);
        report.GetProperty("compiles").GetBoolean().Should().BeTrue(report.ToString());
        report.GetProperty("pages").GetInt32().Should().Be(2, "the PDF export_document gives has 2, whatever one pass counted");
        report.GetProperty("pagesNote").ValueKind.Should().Be(JsonValueKind.Null);

        var other = Doc(Block(block));
        var failing = new Mock<IDocumentExportService>();
        failing.Setup(e => e.ExportToPdfWithEngineAsync(other.Id, "auto")).ThrowsAsync(new InvalidOperationException("no PDF"));
        var fallback = JsonSerializer.SerializeToElement((await Checker(other, source + "% other\n", failing.Object).CheckWithPagesAsync(other)).Report);
        fallback.GetProperty("pages").GetInt32().Should().Be(1);
        fallback.GetProperty("pagesNote").GetString().Should().Contain("one LaTeX pass");

        // A paste's check (CheckAsync) doesn't make the PDF: it cost each paste 10–13 s (#148).
        var quick = Doc(Block(block));
        var unused = new Mock<IDocumentExportService>();
        var plain = JsonSerializer.SerializeToElement((await Checker(quick, source + "% quick\n", unused.Object).CheckAsync(quick)).Report);
        plain.GetProperty("pages").GetInt32().Should().Be(1);
        unused.Verify(e => e.ExportToPdfWithEngineAsync(It.IsAny<Guid>(), It.IsAny<string?>()), Times.Never());
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

    [Fact]
    public void An_arXiv_preprint_reaches_references_bib_with_what_ACMs_style_prints_it_from()
    {
        var entry = new BibliographyEntry
        {
            CiteKey = "jayanti2016",
            EntryType = "misc",
            Data = JsonDocument.Parse(JsonSerializer.Serialize(new { author = "Jayanti, Siddhartha V.", title = "A Randomized Concurrent Algorithm", year = "2016",
                eprint = "1612.01514", archiveprefix = "arXiv", primaryclass = "cs.DC", url = "https://arxiv.org/abs/1612.01514" })),
        };
        var bib = BibTeXSerializer.Serialize([entry]);
        bib.Should().Contain("eprint = {1612.01514}").And.Contain("archiveprefix = {arXiv}").And.Contain("primaryclass = {cs.DC}");
    }

    // ── #131: conference papers and book chapters ────────────────────────

    [Fact]
    public void A_Crossref_conference_paper_and_book_chapter_keep_their_venue()
    {
        var cvpr = new Dictionary<string, string>();
        var cvprWork = JsonDocument.Parse("""{"type":"proceedings-article","container-title":["2016 IEEE Conference on Computer Vision and Pattern Recognition (CVPR)"],"publisher":"IEEE"}""").RootElement;
        BibliographyService.CrossrefKind(cvprWork, cvpr).Should().Be("inproceedings");
        cvpr.Should().Contain("booktitle", "2016 IEEE Conference on Computer Vision and Pattern Recognition (CVPR)").And.Contain("publisher", "IEEE").And.NotContainKey("journal");

        var lncs = new Dictionary<string, string>();
        var lncsWork = JsonDocument.Parse("""{"type":"book-chapter","container-title":["Lecture Notes in Computer Science","Automata, Languages and Programming"],"publisher":"Springer Berlin Heidelberg"}""").RootElement;
        BibliographyService.CrossrefKind(lncsWork, lncs).Should().Be("incollection");
        lncs.Should().Contain("booktitle", "Automata, Languages and Programming").And.Contain("series", "Lecture Notes in Computer Science");

        var article = new Dictionary<string, string>();
        BibliographyService.CrossrefKind(JsonDocument.Parse("""{"type":"journal-article","container-title":["Journal of the ACM"],"publisher":"ACM"}""").RootElement, article).Should().Be("article");
        article.Should().Contain("journal", "Journal of the ACM").And.NotContainKey("publisher");
    }

    // ── #132: an underscore in a footnote ────────────────────────────────

    [Fact]
    public void A_footnotes_text_is_escaped_like_the_paragraphs()
    {
        var block = new Block
        {
            Id = Guid.NewGuid(),
            Type = "paragraph",
            Content = JsonDocument.Parse("""{"text":"A sentence with a footnote\\footnote{check_document reported it, with $x_1$.} in it."}"""),
        };
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderBlockToLatex(block);
        latex.Should().Contain(@"\footnote{check\_document reported it, with $x_1$.}");
    }

    [Fact]
    public void A_chapter_read_from_BibTeX_stays_a_chapter()
    {
        var e = BibTexParser.Parse("@incollection{charikar2002, title = {Finding Frequent Items}, booktitle = {Automata, Languages and Programming}, series = {Lecture Notes in Computer Science}}").Single();
        e.EntryType.Should().Be("incollection", "as @misc ACM's style printed no book or series (#131 retest)");
        e.Fields["booktitle"].Should().Be("Automata, Languages and Programming");
        BibTexParser.Parse("@inbook{k, title = {T}}").Single().EntryType.Should().Be("inbook");
        BibTexParser.Parse("@weird{k, title = {T}}").Single().EntryType.Should().Be("misc");
    }

    // ── #124 note: a label in an unnumbered alignment ────────────────────

    private static readonly JsonElement UnnumberedAlign = JsonSerializer.SerializeToElement(new
    {
        latex = "\\begin{align}\nx &= 1 \\\\\n  y &= 2 \\label{eq:second}\n\\end{align}",
        equationMode = "display",
        numbered = false,
    });

    [Fact]
    public void An_unnumbered_alignment_keeps_no_label_and_says_which_it_dropped()
    {
        var latex = EquationLatex.Render(UnnumberedAlign);
        latex.Should().Contain(@"\begin{align*}").And.NotContain(@"\label{");
        latex.Should().Contain(EquationLatex.DroppedLabelMark + "eq:second");

        var audit = new EmittedLatexAudit("\\documentclass{article}\n\\begin{document}\n\\section{A}\nSee \\eqref{eq:second}.\n" + latex + "\n\\end{document}\n");
        audit.DanglingReferences.Select(r => r.Key).Should().Contain("eq:second");
        audit.StarredMathLabels.Should().Contain("eq:second", "the check says it is in an unnumbered equation");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_an_unnumbered_alignment_no_longer_stops_the_next_labelled_equation()
    {
        var numbered = EquationLatex.Render(JsonSerializer.SerializeToElement(new
        {
            latex = "\\begin{align}\nz &= 3 \\label{eq:third}\n\\end{align}",
            equationMode = "display",
        }));
        var source = "\\documentclass{article}\n\\usepackage{amsmath}\n\\begin{document}\n" + EquationLatex.Render(UnnumberedAlign)
            + "\n" + numbered + "\nSee \\eqref{eq:third}.\n\\end{document}\n";
        var result = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).ValidateAsync(source, "pdflatex");
        result.Valid.Should().BeTrue(result.Error);
    }

    // ── #151's findings: a cell's \%, a title's capitals, the PDF's properties ──

    [Theory]
    [InlineData("71.3\\%", "71.3\\%")]
    [InlineData("Accuracy (\\%)", "Accuracy (\\%)")]
    [InlineData("A \\& B", "A \\& B")]
    [InlineData("50%", "50\\%")]
    [InlineData("a\\b", "a\\textbackslash{}b")]
    public void An_escaped_special_character_in_a_cell_prints_that_character(string cell, string latex) =>
        Lilia.Engines.LatexText.EscapeCell(cell).Should().Be(latex);

    [Fact]
    public void A_titles_names_keep_their_capitals_in_references_bib()
    {
        BibliographyService.ProtectCapitals("On the Einstein Podolsky Rosen paradox").Should().Be("On the {Einstein} {Podolsky} {Rosen} paradox");
        BibliographyService.ProtectCapitals("Deep learning for DNA").Should().Be("Deep learning for {DNA}");
        BibliographyService.ProtectCapitals("Already {Braced} title").Should().Be("Already {Braced} title");
        BibliographyService.ProtectCapitals("Deep Residual Learning for Image Recognition").Should().Be("Deep Residual Learning for Image Recognition", "a Title Case title is left as it is");
        BibliographyService.ProtectCapitals("A study of LaTeX, in Paris.").Should().Be("A study of {LaTeX}, in {Paris}.");
        var entry = new BibliographyEntry
        {
            CiteKey = "bell1964", EntryType = "article",
            Data = JsonDocument.Parse("""{"title":"On the Einstein Podolsky Rosen paradox","author":"Bell, J. S.","year":"1964"}"""),
        };
        BibTeXSerializer.Serialize([entry]).Should().Contain("title = {On the {Einstein} {Podolsky} {Rosen} paradox}");
        entry.Data.RootElement.GetProperty("title").GetString().Should().Be("On the Einstein Podolsky Rosen paradox", "the entry itself is unchanged");
    }

    [Fact]
    public void The_PDF_carries_its_title_and_language()
    {
        var doc = new Document { Title = "Un article", Language = "fr", Blocks = [] };
        var props = RenderService.PdfProperties(doc, "Un article");
        props.Should().Contain("pdftitle={Un article}").And.Contain(@"\LiliaPdfLang{fr}")
            .And.NotContain("pdflang", "hyperref's pdflang after it loaded warned on every check and wrote no /Lang (#158 retest)");
    }

    // #152 retest: R\&D in a cell was split into two cells, "R" and "D".
    [Theory]
    [InlineData(@"Model & R\&D (\%)", new[] { "Model ", @" R\&D (\%)" })]
    [InlineData(@"\multicolumn{2}{c}{A & B} & C", new[] { @"\multicolumn{2}{c}{A & B} ", " C" })]
    [InlineData("a&b&c", new[] { "a", "b", "c" })]
    public void A_table_row_splits_on_its_own_ampersands_only(string row, string[] cells) =>
        Lilia.Import.Services.LatexParser.SplitCells(row).Should().Equal(cells);

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
            .And.Contain(@"\affiliation{\institution{Lilia Editor}\city{Paris}\country{France}}")
            .And.Contain(@"\email{grok@example.org}")
            .And.Contain(@"\author{Ada Lovelace}");
    }

    // #134 (Grok's #133, 17 Nov 2026): Elsevier printed "Lilia Editor, Paris, France," with organization alone.
    [Fact]
    public void Elsevier_gets_organization_city_and_country_and_a_one_line_address_as_text()
    {
        var text = LiliaMcpWriteTools.AuthorText([
            new("Grok Tester", "Lilia Editor", "Paris", "France", "grok@example.org"),
            new("Ada Lovelace", "Analytical Society"),
        ]);
        var (_, body) = PublisherClasses.FrontMatter("elsarticle", "A paper", text, null, [], s => s, "A paper");
        body.Should().Contain(@"\affiliation{organization={Lilia Editor}, city={Paris}, country={France}}")
            .And.Contain(@"\affiliation{Analytical Society}")
            .And.Contain(@"\ead{grok@example.org}");
    }

    // #136: the caption package set its own style for classes it doesn't know.
    [Theory]
    [InlineData("IEEEtran", "labelsep=newline")]
    [InlineData("llncs", "labelfont=bf")]
    [InlineData("revtex4-2", "labelsep=period")]
    public void The_caption_package_follows_the_classs_style(string cls, string setting)
    {
        var packages = "\\usepackage{caption}\n\\usepackage{subcaption}\n";
        PublisherClasses.Packages(packages, cls).Should().Contain(@"\captionsetup").And.Contain(setting);
        PublisherClasses.Packages(packages, "acmart").Should().NotContain(@"\captionsetup", "acmart is a class caption knows");
    }

    // #135: REVTeX chose Physical Review A silently and APS prints no keywords, while the check said passed.
    [Fact]
    public void An_APS_paper_is_told_to_name_its_journal_and_that_keywords_do_not_print()
    {
        var p = PublisherProfiles.Find("aps-final")!;
        var doc = PublisherClassTests.Doc(p.DocumentClass, p.ClassOptions, "Ada Lovelace\\\\Analytical Society\\\\UK", new[] { "union-find" });
        doc.PublisherProfile = p.Id;
        var blocks = doc.Blocks.OrderBy(b => b.SortOrder).ToList();
        var findings = PublisherCompliance.Check(doc, blocks, "").Findings;
        findings.Should().Contain(f => f.Code == "aps_journal").And.Contain(f => f.Code == "aps_keywords");

        doc.LatexDocumentClassOptions = p.ClassOptions + ",prl";
        var named = PublisherCompliance.Check(doc, blocks, "").Findings;
        named.Should().NotContain(f => f.Code == "aps_journal").And.NotContain(f => f.Code == "class_options", "naming the journal is what was asked");
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
