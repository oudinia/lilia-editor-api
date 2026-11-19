using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #180 (19 Nov 2026): a $$ display written over several lines in a theorem or proof was escaped to
/// \$\$ … \$\$ text ("Missing $ inserted"); and #247's note: an exported deck stopped on older TeX with "Option clash for
/// package xcolor".
/// </summary>
public class Batch22DisplayMathTests
{
    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);

    [Fact]
    public void A_display_over_several_lines_in_a_proof_stays_maths()
    {
        var latex = Render.RenderBlockToLatex(B("theorem", new
        {
            theoremType = "proof",
            text = "Text before.\n\n$$\na=b,\n\\qquad\nc=d.\n$$\nMoreover,\n$$\ne=f\n$$\nEnd text.",
        }));
        latex.Should().NotContain(@"\$\$").And.NotContain(@"\textasciicircum");
        latex.Should().Contain("$$\na=b,\n\\qquad\nc=d.\n$$").And.Contain("$$\ne=f\n$$");
    }

    [Fact]
    public void A_display_opening_on_the_line_another_closed_stays_maths()
    {
        var latex = Render.RenderBlockToLatex(B("theorem", new
        {
            theoremType = "lemma",
            text = "Let $x_t$ satisfy\n$$x_{t}=F(x_{t-1}), $$\nwhere\n$$\\epsilon_t\\perp\\sigma(x_s:s<t)$$ Assume the process\n"
                + "$$y_t=\\arg \\min_{y_t} \\mathbb{E}[\\|x_t-y_t\\|^2]\n\\quad\\text{ s.t. }\\quad\ny_t\\perp x_{t-1}$$\nis a Monge solution.",
        }));
        latex.Should().NotContain(@"\$\$").And.Contain("$$y_t=\\arg \\min_{y_t}");
    }

    [Fact]
    public void A_deck_gives_xcolor_its_options_before_the_class()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Deck", OwnerId = "u", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
            LatexDocumentClass = "beamer", Blocks = [B("slide", new { title = "A", content = "a" })],
            BibliographyEntries = new List<BibliographyEntry>(),
        };
        var preview = Render.RenderToLatex(doc);
        var export = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });
        foreach (var tex in new[] { preview, export })
            tex.Should().StartWith("\\PassOptionsToPackage{dvipsnames,svgnames,table}{xcolor}\n\\documentclass");
    }
}
