using FluentAssertions;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Xunit;

namespace Lilia.Api.Tests.Publishers;

/// <summary>IOP (iopjournal) and SIAM (siamart251216), installed from the publishers' sites (25 Oct 2026).</summary>
public class IopSiamTests
{
    private const string Authors = "Ada Lovelace\\\\Analytical Engines\\\\UK \\and Alan Turing\\\\Bletchley Park \\and Grace Hopper\\\\Analytical Engines\\\\UK";

    private static (string Pre, string Body) Front(string cls) =>
        PublisherClasses.FrontMatter(cls, "T", Authors, "\\begin{abstract}A.\\end{abstract}", ["x", "y"], LatexText.Escape, "T");

    [Fact]
    public void IOP_numbers_each_institution_once_and_puts_keywords_before_the_abstract_as_its_template()
    {
        var (pre, body) = Front("iopjournal");
        pre.Should().BeEmpty("iopjournal typesets the front matter where it stands");
        body.Should().Contain(@"\author{Ada Lovelace$^{1}$, Alan Turing$^{2}$ and Grace Hopper$^{1}$}");
        body.Should().Contain(@"\affil{$^{1}$Analytical Engines, UK}").And.Contain(@"\affil{$^{2}$Bletchley Park}");
        body.IndexOf(@"\keywords{x, y}", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf(@"\begin{abstract}", StringComparison.Ordinal));
        body.Should().NotContain(@"\maketitle");
    }

    [Fact]
    public void SIAM_puts_affiliations_in_thanks_and_keywords_in_its_environment()
    {
        var (pre, body) = Front("siamart251216");
        pre.Should().Contain(@"Ada Lovelace\thanks{Analytical Engines, UK.}").And.Contain(@"\and Alan Turing\thanks{Bletchley Park.}");
        body.Should().Contain("\\begin{keywords}\nx, y\n\\end{keywords}");
        body.IndexOf(@"\maketitle", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf(@"\begin{abstract}", StringComparison.Ordinal));
    }

    [Fact]
    public void SIAM_style_never_ships_alone_and_the_others_do()
    {
        PublisherClasses.MayShipStyle("siamplain").Should().BeFalse("SIAM's macros may be distributed only as the whole set");
        foreach (var p in PublisherProfiles.All.Where(p => p.Publisher != "SIAM"))
            PublisherClasses.MayShipStyle(p.BibliographyStyle).Should().BeTrue(p.Id);
    }

    [Fact]
    public void SIAM_drops_amsthm_which_clashes_with_its_ntheorem()
    {
        var packages = PublisherClasses.Packages(LaTeXPreamble.Packages, "siamart251216");
        packages.Should().NotContain("amsthm").And.Contain(@"\usepackage{amsmath,amssymb,amsfonts}");
    }
}
