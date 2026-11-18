using System.Text.Json;
using FluentAssertions;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// What Lilia's LaTeX made of five imported arXiv papers (launch-bot #137, 17 Nov 2026): a preserved algorithm2e
/// beside Lilia's algorithm + algorithmic (#138), two ``quotes'' read as a code span (#141), an imported \date{}
/// printed as today (#143), $\$$ paired with the wrong dollar (#144), and single line breaks in theorems and list
/// items doubled into paragraph breaks (#145).
/// </summary>
public class ArxivEmitterTests
{
    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static RenderService Render() => new(null!, NullLogger<RenderService>.Instance);

    private static Document Doc(string? packages, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Arxiv", OwnerId = "u", Language = "en", PaperSize = "a4",
            FontFamily = "serif", FontSize = 10, LatexDocumentClass = "article", LatexPackages = packages,
            BibliographyEntries = new List<BibliographyEntry>(), CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = blocks.ToList(),
        };
        var i = 0;
        foreach (var b in doc.Blocks) { b.SortOrder = i++; b.Document = doc; }
        return doc;
    }

    // #138
    [Theory]
    [InlineData("algorithm2e", "ruled,vlined")]
    [InlineData("algpseudocode", null)]
    public void A_preserved_algorithm_package_that_clashes_is_left_out(string name, string? options)
    {
        var packages = JsonSerializer.Serialize(new object[]
        {
            options is null ? new { name } : new { name, options },
            new { name = "tikz" },
        });
        var tex = Render().RenderToLatex(Doc(packages, B("paragraph", new { text = "Text." })));

        tex.Should().NotContain($"{{{name}}}");
        tex.Should().Contain(@"\IfFileExists{tikz.sty}");
        tex.Should().Contain(@"\usepackage{algorithm}").And.Contain(@"\usepackage{algorithmic}");
    }

    // #141
    [Fact]
    public void Two_tex_quotes_in_a_paragraph_are_quotes_not_code()
    {
        var latex = Render().RenderBlockToLatex(B("paragraph", new { text = "We write ``lower'' and ``upper'' here." }));

        latex.Should().Contain("We write ``lower'' and ``upper'' here.").And.NotContain(@"\texttt");
    }

    [Fact]
    public void A_code_span_is_still_code()
        => Render().RenderBlockToLatex(B("paragraph", new { text = "Run `make` then ``go''." }))
            .Should().Contain(@"Run \texttt{make} then ``go''.");

    // #143
    [Fact]
    public void An_imported_empty_date_prints_no_date()
    {
        var tex = Render().RenderToLatex(Doc(null,
            B("title", new { title = "Minimal", author = @"Ann Author \and Bob Author\\Example University", date = "", dateMode = "none" }),
            B("paragraph", new { text = "Text." })));

        tex.Should().Contain(@"\date{}").And.NotContain(@"\date{\today}");
        tex.Should().Contain(@"\author{Ann Author \and Bob Author\\Example University}");
    }

    [Fact]
    public void An_empty_date_without_mode_is_still_today()
        => Render().RenderToLatex(Doc(null, B("title", new { title = "T", author = "A", date = "" })))
            .Should().Contain(@"\date{\today}");

    [Fact]
    public void Aps_front_matter_prints_an_authors_thanks()
    {
        var (_, body) = Lilia.Engines.Publishers.PublisherClasses.FrontMatter("revtex4-2", "T",
            @"Ann Author\thanks{Funded by X.}\\Example University", null, [], s => s, "T");

        body.Should().Contain("\\author{Ann Author}\n\\thanks{Funded by X.}\n").And.Contain(@"\affiliation{Example University}");
    }

    // #144
    [Fact]
    public void A_dollar_sign_in_maths_keeps_the_formulas_paired()
    {
        var latex = Render().RenderBlockToLatex(B("paragraph", new { text = @"It costs $\$$1000 per year, and $x=1$ here; it was $\$$5 before." }));

        latex.Should().Contain(@"It costs $\$$1000 per year, and $x=1$ here; it was $\$$5 before.");
    }

    // #145
    [Fact]
    public void A_list_item_on_two_lines_stays_one_paragraph()
    {
        var latex = Render().RenderBlockToLatex(B("list", new { items = new[] { "First item, written\n on two lines." }, ordered = true }));

        latex.Should().Contain("\\item First item, written\n on two lines.").And.NotContain("written\n\n");
    }

    [Fact]
    public void A_theorem_keeps_tikz_options_on_several_lines_in_one_paragraph()
    {
        var latex = Render().RenderBlockToLatex(B("theorem", new
        {
            theoremType = "lemma",
            text = "A drawing:\n\\begin{tikzpicture}[\n  scale=1]\n\\draw (0,0)--(1,1);\n\\end{tikzpicture}",
        }));

        latex.Should().NotContain("\n\n");
        latex.Should().Contain("\\begin{tikzpicture}[\n  scale=1]");
    }

    [Fact]
    public void A_blank_line_in_a_theorem_is_still_a_paragraph_break()
        => Render().RenderBlockToLatex(B("theorem", new { theoremType = "theorem", text = "One.\n\nTwo." }))
            .Should().Contain("One.\n\nTwo.");

    [Fact]
    public void A_paragraph_still_doubles_its_line_breaks()
        => Render().RenderBlockToLatex(B("paragraph", new { text = "One.\nTwo." }))
            .Should().Contain("One.\n\nTwo.");
}

/// <summary>
/// The 18 Nov 2026 batch (launch-bot #161, #162, #163, #154): an imported preamble's natbib-only settings without
/// natbib, a proof's equation keeping its label, a ``quote closed by a straight ", and Latin-script references in an
/// Arabic document set left to right.
/// </summary>
public class ArxivEmitterBatch5Tests
{
    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static RenderService Render() => new(null!, NullLogger<RenderService>.Instance);

    private static Document Doc(string language, string? customPreamble, List<BibliographyEntry> entries, params Block[] blocks)
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Doc", OwnerId = "u", Language = language, PaperSize = "a4",
            FontFamily = "serif", FontSize = 10, LatexDocumentClass = "article", CustomPreamble = customPreamble,
            BibliographyEntries = entries, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            Blocks = blocks.ToList(),
        };
        var i = 0;
        foreach (var b in doc.Blocks) { b.SortOrder = i++; b.Document = doc; }
        return doc;
    }

    private static BibliographyEntry Entry(string key, object fields) => new()
    {
        Id = Guid.NewGuid(), CiteKey = key, EntryType = "article",
        Data = JsonDocument.Parse(JsonSerializer.Serialize(fields)),
    };

    // #161
    [Fact]
    public void A_natbib_setting_from_an_imported_preamble_applies_only_when_natbib_is_loaded()
    {
        var tex = Render().RenderToLatex(Doc("en", "\\setlength{\\bibsep}{2pt}\n\\newcommand{\\R}{\\mathbb{R}}",
            new List<BibliographyEntry> { Entry("K1", new { author = "A. Author", title = "A title", year = "2020" }) },
            B("paragraph", new { text = "Text \\cite{K1}." })));

        tex.Should().NotContain(@"\usepackage{natbib}");
        tex.Should().Contain(@"\ifcsname ver@natbib.sty\endcsname\setlength{\bibsep}{2pt}\fi");
        tex.Should().Contain(@"\newcommand{\R}{\mathbb{R}}");
    }

    [Theory]
    [InlineData(@"\bibpunct{[}{]}{,}{n}{,}{,}")]
    [InlineData(@"\setcitestyle{numbers,square}")]
    [InlineData(@"\citestyle{nature}")]
    [InlineData(@"\setlength{\bibhang}{1em}")]
    public void Each_natbib_only_setting_is_guarded(string line)
        => LaTeXPreamble.CustomPreambleForEmit(line).Should().Be($@"\ifcsname ver@natbib.sty\endcsname{line}\fi");

    [Fact]
    public void A_natbib_setting_with_a_comment_keeps_the_guard_closed()
        => LaTeXPreamble.CustomPreambleForEmit("\\setlength{\\bibsep}{0pt} % tight\r\n\\newcommand{\\x}{y}")
            .Should().Be("\\ifcsname ver@natbib.sty\\endcsname\\setlength{\\bibsep}{0pt}\\fi % tight\n\\newcommand{\\x}{y}");

    [Fact]
    public void A_document_citing_with_citep_keeps_natbib_and_its_setting()
    {
        var tex = Render().RenderToLatex(Doc("en", "\\setlength{\\bibsep}{2pt}",
            new List<BibliographyEntry> { Entry("K1", new { author = "A. Author", title = "A title", year = "2020" }) },
            B("paragraph", new { text = "Text \\citep{K1}." })));

        tex.Should().Contain(@"\usepackage{natbib}").And.Contain(@"\setlength{\bibsep}{2pt}");
    }

    // #162
    [Fact]
    public void A_proofs_equation_keeps_its_label_and_no_blank_line()
    {
        var latex = Render().RenderBlockToLatex(B("theorem", new
        {
            theoremType = "proof",
            text = "We define\n\\begin{equation}\n\t\\label{eq:w}\n\tw = 1\n\\end{equation}\nand use \\eqref{eq:w}.",
            label = "",
        }));

        latex.Should().Contain("\\begin{equation}\n\t\\label{eq:w}\n\tw = 1\n\\end{equation}");
        latex.Should().StartWith("\\begin{proof}\nWe define");
    }

    // #163
    [Fact]
    public void A_straight_quote_after_two_backticks_closes_the_quote()
        => Render().RenderBlockToLatex(B("paragraph", new { text = "Informally, ``goes down\"), and ``goes up\")." }))
            .Should().Contain("Informally, ``goes down''), and ``goes up'').");

    [Fact]
    public void Straight_quote_pairs_and_umlauts_are_unchanged()
        => Render().RenderBlockToLatex(B("paragraph", new { text = "Say \"a\" and \"b\" to M\\\"obius." }))
            .Should().Contain("Say ``a'' and ``b'' to M\\\"obius.");

    // #154
    [Fact]
    public void An_arabic_documents_latin_references_are_set_left_to_right()
    {
        var tex = Render().RenderToLatex(Doc("ar", null, new List<BibliographyEntry>
            {
                Entry("huffman1952", new { author = "Huffman, David", title = "A Method for the Construction of Minimum-Redundancy Codes", journal = "Proceedings of the IRE", year = "1952" }),
                Entry("arabic2020", new { author = "مؤلف", title = "عنوان", year = "2020" }),
            },
            B("paragraph", new { text = "اقترح هوفمان \\cite{huffman1952} و\\cite{arabic2020} خوارزمية." })));

        tex.Should().Contain(@"\def\liliaLtrRefs");
        tex.Should().Contain("\\liliaLtrRefs{huffman1952}\n\\begin{thebibliography}");
        // Amiri has no italic: babel's Latin runs took the substituted shape's font and stayed italic after an \em.
        tex.Should().Contain("ItalicFont=Amiri-Regular.ttf,ItalicFeatures={FakeSlant=0.2}");
    }

    [Fact]
    public void An_english_document_has_no_reference_direction_marks()
        => Render().RenderToLatex(Doc("en", null,
                new List<BibliographyEntry> { Entry("k", new { author = "A. Author", title = "T", year = "2020" }) },
                B("paragraph", new { text = "Text \\cite{k}." })))
            .Should().NotContain("liliaLtrRefs");
}
