using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>Run 2, MCP-80: content the Typst path prints wrong goes to LaTeX; plain prose stays on Typst.</summary>
public class ContentRoutingTests
{
    private static (string, JsonElement) B(string type, object content) =>
        (type, JsonDocument.Parse(JsonSerializer.Serialize(content)).RootElement);

    [Theory]
    [InlineData("A __underlined__ word.")]
    [InlineData("A ==highlighted== word.")]
    [InlineData(@"Speed \SI{3.0}{\metre}.")]
    [InlineData(@"Euler \(e^{i\pi}+1=0\).")]
    [InlineData(@"Display \[ x^2 = 4 \].")]
    [InlineData("It costs $5 and the deluxe costs $10.")]
    [InlineData("Let $x$ be real.")]
    [InlineData("See Theorem~(thm:a) and @cite{knuth84}.")]
    public void Content_typst_misprints_goes_to_latex(string text) =>
        ContentRouting.WhyLatex([B("paragraph", new { text })]).Should().NotBeNull();

    [Fact]
    public void Plain_prose_stays_on_typst() =>
        ContentRouting.WhyLatex([B("heading", new { text = "Intro", level = 1 }), B("paragraph", new { text = "Plain **bold** and *italic*, 50% done." }),
            B("figure", new { src = "https://x/a_b__c.png", caption = "A picture" })]).Should().BeNull();

    [Fact]
    public void An_equation_goes_to_latex() =>
        ContentRouting.WhyLatex([B("equation", new { latex = "x^2" })]).Should().Be("equations");
}
