using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>
/// Publisher classes (publisher templates phase 1, 25 Oct 2026): acmart, IEEEtran, elsarticle and llncs
/// print in the publisher's own class, with its front matter, no layout of Lilia's, a slimmed package
/// list and the publisher's bibliography style; and a full document compiles in each, preview and export.
/// </summary>
public class PublisherClassTests
{
    internal static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
        CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
    };

    internal static Document Doc(string cls, string? options = null, string author = "Ada Lovelace \\and Alan Turing", object? keywords = null)
    {
        var blocks = new[]
        {
            B("title", new { title = "Visual Blocks for Real LaTeX", author, date = "", keywords }),
            B("abstract", new { text = "We show that blocks can emit **valid** LaTeX for publishers, with $x^2$." }),
            B("heading", new { text = "Introduction", level = 1 }),
            B("paragraph", new { text = "As shown by @cite{knuth84}, typesetting matters; see Theorem~(thm:a) and Equation~(eq:x)." }),
            B("equation", new { latex = "E = mc^2", label = "eq:x", mode = "display" }),
            B("theorem", new { theoremType = "theorem", text = "Every block maps to LaTeX.", label = "thm:a" }),
            B("theorem", new { theoremType = "proof", text = "By construction." }),
            B("heading", new { text = "Results", level = 1 }),
            B("table", new { caption = "Yields", label = "tab:y", headers = new[] { "Compound", "Yield" }, rows = new[] { new[] { "A", "82" }, new[] { "B", "5" } } }),
            B("list", new { items = new[] { "one $a$", "two **b**" }, ordered = true }),
            B("code", new { code = "print('hi')", language = "python", caption = "Code", label = "lst:py" }),
            B("algorithm", new { caption = "Sum", label = "alg:sum", lines = new object[] { new { kind = "statement", text = "$s \\gets s + i$" }, new { kind = "return", text = "$s$" } } }),
            B("bibliography", new { text = "" }),
        };
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Visual Blocks for Real LaTeX", OwnerId = "u", Language = "en", PaperSize = "letter",
            FontFamily = "serif", FontSize = 10, LatexDocumentClass = cls, LatexDocumentClassOptions = options,
            Blocks = blocks.Select((b, i) => { b.SortOrder = i; return b; }).ToList(),
            BibliographyEntries = new List<BibliographyEntry>
            {
                new() { Id = Guid.NewGuid(), CiteKey = "knuth84", EntryType = "book",
                    Data = JsonDocument.Parse("""{"author":"Knuth, Donald E.","title":"The TeXbook","publisher":"Addison-Wesley","year":"1984"}"""),
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            },
            CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
        };
        foreach (var b in doc.Blocks) { b.Document = doc; b.DocumentId = doc.Id; }
        foreach (var e in doc.BibliographyEntries) { e.DocumentId = doc.Id; }
        return doc;
    }


    private static string Export(Document doc) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.OrderBy(b => b.SortOrder).ToList(), doc.BibliographyEntries.ToList(), new LaTeXExportOptions());

    private static string Preview(Document doc) => new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    [Theory]
    [InlineData("acmart", "sigconf", @"\documentclass[sigconf]{acmart}")]
    [InlineData("ieeetran", "conference", @"\documentclass[conference]{IEEEtran}")]
    [InlineData("elsarticle", null, @"\documentclass{elsarticle}")]
    public void The_class_takes_only_its_own_options(string cls, string? options, string expected)
    {
        var doc = Doc(cls, options);
        doc.FontSize = 12; doc.PaperSize = "a4"; doc.Columns = 2; doc.Orientation = "landscape";
        LaTeXPreambleBuilder.BuildClassDirective(doc).Should().Be(expected, "size, paper, orientation and columns are the publisher's");
    }

    [Fact]
    public void No_layout_of_Liliass_own()
    {
        var doc = Doc("acmart", "sigconf");
        doc.MarginTop = "3cm"; doc.LineSpacing = 2.0;
        LaTeXPreambleBuilder.BuildLayoutPreamble(doc).Should().BeEmpty("acmart refuses \\section redefinitions (placeins) and layout changes");
        var article = Doc("article");
        article.MarginTop = "3cm";
        LaTeXPreambleBuilder.BuildLayoutPreamble(article).Should().Contain("geometry");
    }

    [Fact]
    public void The_package_list_is_slimmed_per_class()
    {
        var acm = LaTeXPreamble.ForClass(LaTeXPreamble.Packages, "acmart");
        acm.Should().NotContain(@"\usepackage{lmodern}").And.NotContain("amssymb").And.NotContain(@"\usepackage{parskip}").And.NotContain("colorlinks");
        acm.Should().Contain(@"\usepackage{amsmath}");
        LaTeXPreamble.ForClass(LaTeXPreamble.Packages, "llncs").Should().Contain("\\let\\proof\\relax\n\\let\\endproof\\relax\n\\usepackage{amsmath,amssymb,amsfonts,amsthm}");
        LaTeXPreamble.ForClass(LaTeXPreamble.Packages, "IEEEtran").Should().Contain(@"\PassOptionsToPackage{hidelinks}{hyperref}");
        LaTeXPreamble.ForClass(LaTeXPreamble.Packages, "article").Should().Be(LaTeXPreamble.Packages);
        LaTeXPreamble.TheoremEnvironmentsFor("llncs").Should().Contain(@"\@ifundefined{theorem}", "llncs defines theorem itself");
        LaTeXPreamble.JournalShimsFor("llncs").Should().NotContain(@"\providecommand{\keywords}", "llncs defines \\keywords late, with its own \\and")
            .And.NotContain(@"\newenvironment{keywords}", "an environment named keywords defines \\keywords too");
        LaTeXPreamble.JournalShimsFor("IEEEtran").Should().Be(LaTeXPreamble.JournalShims);
    }

    [Fact]
    public void acmart_puts_the_abstract_and_keywords_before_maketitle()
    {
        var tex = Export(Doc("acmart", "sigconf", "Ada Lovelace\\\\Analytical Engines\\\\UK \\and Alan Turing", new[] { "LaTeX", "publishing" }));
        var begin = tex.IndexOf(@"\begin{document}");
        tex.IndexOf(@"\begin{abstract}").Should().BeGreaterThan(begin).And.BeLessThan(tex.IndexOf(@"\maketitle"));
        tex.IndexOf(@"\keywords{LaTeX, publishing}").Should().BeLessThan(tex.IndexOf(@"\maketitle"));
        tex.Should().Contain(@"\author{Ada Lovelace}").And.Contain(@"\affiliation{\institution{Analytical Engines}\country{UK}}");
        tex.Should().Contain(@"\bibliographystyle{ACM-Reference-Format}");
        Preview(Doc("acmart", "sigconf")).Should().Contain(@"\begin{abstract}").And.NotContain(@"\date{");
    }

    [Fact]
    public void elsarticle_writes_a_frontmatter_environment_without_maketitle()
    {
        var tex = Export(Doc("elsarticle", null, "Ada Lovelace\\\\Analytical Engines \\and Alan Turing", new[] { "LaTeX", "blocks" }));
        var front = tex[tex.IndexOf(@"\begin{frontmatter}")..tex.IndexOf(@"\end{frontmatter}")];
        front.Should().Contain(@"\author{Ada Lovelace}").And.Contain(@"\author{Alan Turing}").And.Contain(@"\affiliation{organization={Analytical Engines}}");
        front.Should().Contain(@"\begin{keyword}LaTeX \sep blocks\end{keyword}").And.Contain(@"\begin{abstract}");
        tex.Should().NotContain(@"\maketitle").And.Contain(@"\bibliographystyle{elsarticle-num}");
    }

    [Fact]
    public void llncs_numbers_institutes_and_keeps_keywords_in_the_abstract()
    {
        var tex = Export(Doc("llncs", null, "Ada Lovelace\\\\Univ A \\and Alan Turing\\\\Univ B \\and Grace Hopper\\\\Univ A", new[] { "LaTeX" }));
        tex.Should().Contain(@"\author{Ada Lovelace\inst{1} \and Alan Turing\inst{2} \and Grace Hopper\inst{1}}");
        tex.Should().Contain(@"\institute{Univ A \and Univ B}");
        tex.Should().Contain("\\keywords{LaTeX}\n\\end{abstract}").And.Contain(@"\bibliographystyle{splncs04}");
    }

    [Fact]
    public void IEEEtran_writes_author_blocks_and_IEEEkeywords()
    {
        var tex = Export(Doc("IEEEtran", "conference", "Ada Lovelace\\\\Analytical Engines \\and Alan Turing", new[] { "LaTeX" }));
        tex.Should().Contain(@"\IEEEauthorblockN{Ada Lovelace}\IEEEauthorblockA{Analytical Engines}");
        tex.Should().Contain(@"\begin{IEEEkeywords}LaTeX\end{IEEEkeywords}").And.Contain(@"\bibliographystyle{IEEEtran}");
    }

    [Fact]
    public void Keywords_come_from_the_title_block_as_a_list_or_one_string()
    {
        RenderService.TitleKeywords(Doc("acmart", null, keywords: new[] { " a ", "b" })).Should().Equal("a", "b");
        RenderService.TitleKeywords(Doc("acmart", null, keywords: "a; b, c")).Should().Equal("a", "b", "c");
        RenderService.TitleKeywords(Doc("acmart")).Should().BeEmpty();
    }

    [Trait("Category", "RequiresTeX")]
    [Theory]
    [InlineData("acmart", "sigconf"), InlineData("acmart", "manuscript,acmsmall"), InlineData("IEEEtran", "conference"),
     InlineData("IEEEtran", "journal"), InlineData("elsarticle", "preprint"), InlineData("llncs", null)]
    public async Task A_full_document_compiles_in_the_class_preview_and_export(string cls, string? options)
    {
        var doc = Doc(cls, options, "Ada Lovelace\\\\Analytical Engines\\\\UK \\and Alan Turing", new[] { "LaTeX", "publishing" });
        var renderer = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        foreach (var (path, tex) in new[] { ("export", Export(doc)), ("preview", Preview(doc)) })
        {
            new EmittedLatexAudit(tex).DanglingReferences.Should().BeEmpty($"{cls} {path}");
            var pdf = await renderer.RenderToPdfAsync(tex, "pdflatex", timeout: 180);
            System.Text.Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-", $"{cls} [{options}] {path} compiles");
        }
    }
}
