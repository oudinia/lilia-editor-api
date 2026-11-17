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
