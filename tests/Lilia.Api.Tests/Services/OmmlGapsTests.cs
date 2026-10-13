using System.Text.RegularExpressions;
using FluentAssertions;
using Lilia.Engines;
using Lilia.Import.Converters;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>MCP-100 / MCP-101 (run 3): what Word's maths converter printed as words or dropped.</summary>
public class OmmlGapsTests
{
    private static string Text(string latex)
    {
        var (omml, ok, error) = new LatexToOmmlConverter().Convert(latex);
        ok.Should().BeTrue(error);
        return string.Concat(Regex.Matches(omml, "<m:t[^>]*>([^<]*)</m:t>").Select(m => m.Groups[1].Value));
    }

    [Theory]
    [InlineData(@"a \lesssim b", "a≲b")]
    [InlineData(@"1, \dots, n", "1,…,n")]
    [InlineData(@"\mathcal{L}(x)", "ℒ(x)")]
    [InlineData(@"\mathbb{T}", "𝕋")]
    [InlineData(@"\mathfrak{g}", "𝔤")]
    [InlineData(@"\|x\|", "‖x‖")]
    [InlineData(@"\big( x \big)", "(x)")]
    [InlineData(@"[0, 1]", "[0,1]")]
    [InlineData("a + b % a comment\n + c", "a+b+c")]
    [InlineData(@"x \mid y", "x∣y")]
    public void Prints_the_symbol_not_its_name(string latex, string text) =>
        Text(latex).Should().Be(text);

    [Fact]
    public void Left_brace_delimiters_stay_braces()
    {
        var (omml, _, _) = new LatexToOmmlConverter().Convert(@"\left\{ x \right\}");
        omml.Should().Contain("m:val=\"{\"").And.Contain("m:val=\"}\"");
    }

    [Fact]
    public void Mathrm_holding_maths_is_upright_maths_not_text()
    {
        var (omml, _, _) = new LatexToOmmlConverter().Convert(@"9.8\,\mathrm{m\,s^{-2}}");
        omml.Should().Contain("<m:sSup").And.NotContain(@"\,").And.NotContain("^{");
    }

    [Fact]
    public void Display_chemistry_has_arrows_and_subscripts()
    {
        var (omml, ok, _) = new LatexToOmmlConverter().Convert(ProseLatex.MathForWord(@"\ce{C7H6O3 + C4H6O3 ->[H+] C9H8O4}"));
        ok.Should().BeTrue();
        omml.Should().Contain("→").And.Contain("<m:sSub").And.NotContain("->").And.NotContain("mathrm");
    }

    [Theory]
    [InlineData(@"\ce{Fe^3+}", "Fe³⁺")]
    [InlineData(@"\ce{H2O -> H2 + O2}", "H₂O → H₂ + O₂")]
    [InlineData(@"\ce{A <=> B}", "A ⇌ B")]
    public void Prose_chemistry_reads_as_text(string latex, string text) =>
        ProseLatex.PlainForWord(latex).Should().Be(text);
}
