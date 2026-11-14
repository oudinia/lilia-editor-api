using System.Xml.Linq;
using FluentAssertions;
using Lilia.Import.Converters;
using Xunit;

namespace Lilia.Api.Tests.Engines.Maths;

/// <summary>
/// The Word export's maths (MCP-118, retested 14 Nov): <c>\mathrm{m\,s^{-2}}</c> lost its thin space and
/// read "ms⁻²" (milliseconds); a sum with one limit had an empty upper limit without supHide, and
/// <c>\sin x</c> an empty argument followed by x: placeholder boxes in Word.
/// </summary>
public class OmmlSpacesAndPlaceholdersTests
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/officeDocument/2006/math";

    private static XElement Omml(string latex)
    {
        var (omml, ok, _) = new LatexToOmmlConverter().Convert(latex);
        ok.Should().BeTrue();
        return XElement.Parse($"<m:oMath xmlns:m=\"{M}\">{omml}</m:oMath>");
    }

    private static string Text(XElement e) => string.Concat(e.Descendants(M + "t").Select(t => t.Value));

    [Fact]
    public void Units_keep_their_thin_space()
    {
        Text(Omml(@"9.81\,\mathrm{m\,s^{-2}}")).Should().Be("9.81 m s-2");
        Text(Omml(@"a \quad b \; c \! d")).Should().Be("a b cd");
    }

    [Fact]
    public void A_missing_limit_is_hidden_not_an_empty_box()
    {
        var nary = Omml(@"\sum_{i=1} x_i").Descendants(M + "nary").Single();
        var pr = nary.Element(M + "naryPr")!;
        pr.Element(M + "supHide")!.Attribute(M + "val")!.Value.Should().Be("1");
        pr.Element(M + "subHide").Should().BeNull();
        Omml(@"\int f").Descendants(M + "naryPr").Single().Elements().Select(e => e.Name.LocalName)
            .Should().Contain(["subHide", "supHide"]);
    }

    [Theory]
    [InlineData(@"\sin x", "x")]
    [InlineData(@"\sin(x + y)", "(x+y)")]
    [InlineData(@"\log \theta", "θ")]
    public void A_function_takes_its_argument(string latex, string argument)
    {
        var func = Omml(latex).Descendants(M + "func").Single();
        Text(func.Element(M + "e")!).Should().Be(argument);
        Text(func.Element(M + "fName")!).Should().Be(latex[1..4]);
    }

    [Fact]
    public void A_limit_sits_under_the_name_and_a_function_with_no_argument_is_its_name()
    {
        var lim = Omml(@"\lim_{x \to 0} f").Descendants(M + "func").Single();
        lim.Element(M + "fName")!.Element(M + "limLow").Should().NotBeNull();
        Omml(@"\sin = 1").Descendants(M + "func").Should().BeEmpty();
        Omml(@"\sin^2 x").Descendants(M + "func").Single().Element(M + "fName")!.Element(M + "sSup").Should().NotBeNull();
        Omml(@"\sin").Descendants(M + "e").Should().BeEmpty("no empty argument box");
    }
}
