using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The document sweep (A-009, E-015, E-030, 15 Nov 2026): ticks, crosses, warnings and stars that AI-written text puts
/// in prose (✅ ⚠️ ✓ ✗), and accents typed as a letter plus a combining mark (e + U+0301), were each a LaTeX error, and
/// the character was dropped from the PDF.
/// </summary>
public class SymbolsAndAccentsTests
{
    private const string Text = "Results ✅ and caveats ⚠️, done ✓, failed ✗ ✘ ❌, note ❗ ❓, rating ★★☆, ℵ, café, naïve, ℝ ℕ ℤ ℚ ℂ ℍ ℙ ℓ.";

    [Theory]
    [InlineData("✅")] [InlineData("✓")] [InlineData("✔")] [InlineData("✗")] [InlineData("✘")] [InlineData("❌")]
    [InlineData("⚠")] [InlineData("❗")] [InlineData("❓")] [InlineData("★")] [InlineData("☆")] [InlineData("ℵ")] [InlineData("\uFE0F")]
    [InlineData("ℝ")] [InlineData("ℕ")] [InlineData("ℤ")] [InlineData("ℚ")] [InlineData("ℂ")] [InlineData("ℍ")] [InlineData("ℙ")] [InlineData("ℓ")]
    public void The_shared_preamble_maps_the_symbol(string symbol)
        => LaTeXPreamble.Packages.Should().Contain($"\\newunicodechar{{{symbol}}}");

    [Fact]
    public void A_letter_and_its_combining_accent_become_one_character()
        => LaTeXRenderService.ComposeAccents("cafe\u0301 nai\u0308ve").Should().Be("café naïve");

    [Fact]
    public void Source_already_composed_is_returned_as_is()
    {
        const string latex = "\\section{Café}\nPlain text.";
        LaTeXRenderService.ComposeAccents(latex).Should().BeSameAs(latex);
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task The_symbols_and_decomposed_accents_compile_with_no_problem()
    {
        var body = Text.Replace("café", "cafe\u0301").Replace("naïve", "nai\u0308ve");
        var latex = "\\documentclass{article}\n" + LaTeXPreamble.Packages + "\\begin{document}\n" + body + "\n\\section{Status ✅}\n\\end{document}\n";
        var (pdf, _, problems) = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfTolerantWithProblemsAsync(latex, timeout: 120, engine: "pdflatex");
        pdf.Should().NotBeEmpty();
        problems.Should().BeEmpty();
    }
}
