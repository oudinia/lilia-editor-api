using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// MCP-90 (run 3): the check's bibliography escaped BibTeX markup (Fej\{\textbackslash{}'e\}r,
/// \$\textbackslash{}Delta\$) and listed entries no one cites, so it differed from what BibTeX
/// prints from the export's .bib.
/// </summary>
public class CheckBibliographyTests
{
    [Theory]
    [InlineData(@"Fej{\'e}r", @"Fej{\'e}r")]
    [InlineData(@"On $\Delta$-sets", @"On $\Delta$-sets")]
    [InlineData(@"{NLP} for all", @"{NLP} for all")]
    [InlineData("Smith & Jones: 50% of #1", @"Smith \& Jones: 50\% of \#1")]
    [InlineData("snake_case and $x_1$", @"snake\_case and $x_1$")]
    public void Bibtex_fields_print_as_latex(string field, string latex) =>
        BibTeXField.ToLatex(field).Should().Be(latex);

    [Fact]
    public void Unbalanced_braces_fall_back_to_escaping() =>
        BibTeXField.ToLatex("a } b").Should().NotContain(" } ");

    [Theory]
    [InlineData("Donald E. Knuth", "Knuth")]
    [InlineData("Hardy, G. H. and Wright, E. M.", "Hardy and Wright")]
    [InlineData("A. One and B. Two and C. Three", "One et~al.")]
    [InlineData("Lipót Fejér and others", "Fejér et~al.")]
    public void Natbib_labels_shorten_authors(string authors, string label) =>
        BibTeXField.ShortAuthors(authors).Should().Be(label);

    [Fact]
    public void Cited_keys_come_from_every_cite_command() =>
        RenderService.CitedKeys(@"See \citep[p.~3]{a, b} and \citet{c}; \nocite{d}. \cite*{e}")
            .Should().BeEquivalentTo(["a", "b", "c", "d", "e"]);
}
