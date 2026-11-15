using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #113 retest (15 Nov 2026): a tblr table pasted as raw LaTeX without its \usepackage stopped the
/// LaTeX PDF with "Environment tblr undefined". Lilia now loads tabularray when a block uses tblr.
/// </summary>
public class TabularrayAutoLoadTests
{
    private const string Tblr = @"\begin{tblr}{colspec={|l|c|r|}, hlines} Item & Count & Price \\ Apples & 3 & 1.20 \\ \end{tblr}";

    [Theory]
    [InlineData(@"\begin{tblr}{lr} a & 1 \end{tblr}")]
    [InlineData(@"\begin{longtblr}{lr} a & 1 \end{longtblr}")]
    [InlineData(@"\begin{talltblr}{lr} a & 1 \end{talltblr}")]
    [InlineData(@"\begin {tblr}{lr} a & 1 \end{tblr}")]
    public void A_tblr_table_brings_tabularray(string block)
        => LaTeXPreamble.MhchemLine([block]).Should().Contain(@"\usepackage{tabularray}");

    [Fact]
    public void Not_twice_when_the_author_already_loads_it()
        => LaTeXPreamble.MhchemLine([Tblr], @"\usepackage{tabularray}").Should().NotContain("tabularray");

    [Fact]
    public void Not_without_a_tblr_table()
        => LaTeXPreamble.MhchemLine([@"\begin{tabular}{lr} a & 1 \end{tabular}", "Plain text."]).Should().NotContain("tabularray");

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task A_pasted_tblr_compiles_without_its_usepackage()
    {
        var latex = "\\documentclass{article}\n" + LaTeXPreamble.MhchemLine([Tblr]) + "\\begin{document}\n" + Tblr + "\n\\end{document}\n";
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(latex, "pdflatex", timeout: 60);
        pdf.Should().NotBeEmpty();
    }
}
