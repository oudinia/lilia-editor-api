using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>MCP-98 (run 3): BibTeX markup printed as typed in the Word bibliography.</summary>
public class BibTeXPlainTests
{
    [Theory]
    [InlineData(@"Fej{\'e}r", "Fejér")]
    [InlineData(@"M{\""u}ller", "Müller")]
    [InlineData(@"M\""uller", "Müller")]
    [InlineData(@"Ga\c{c}a and \v{S}koda", "Gaça and Škoda")]
    [InlineData(@"Erd\H{o}s", "Erdős")]
    [InlineData(@"{\ss}", "ß")]
    [InlineData(@"{NLP} for {B}ayes", "NLP for Bayes")]
    [InlineData("51--69", "51–69")]
    [InlineData(@"On $\Delta$-sets and $\alpha$", "On Δ-sets and α")]
    [InlineData(@"Costs \$5 \& more", "Costs $5 & more")]
    [InlineData(@"\emph{Deep} learning", "Deep learning")]
    public void Reads_as_the_reader_sees_it(string bibtex, string text) =>
        BibTeXField.ToPlain(bibtex).Should().Be(text);
}
