using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// A PDF LaTeX made in spite of errors says so (the launch bot's #103, 14 Nov 2026: a broken preamble line printed
/// as stray text, "ewcommand[1]1", and nothing said anything was wrong).
/// </summary>
public class CompileProblemsTests
{
    [Fact]
    public void A_clean_log_has_no_problems_and_a_log_with_an_error_has_its_lines()
    {
        LaTeXRenderService.ProblemsIn("This is pdfTeX\n(./document.tex)\nOutput written on document.pdf").Should().BeEmpty();
        LaTeXRenderService.ProblemsIn(null).Should().BeEmpty();
        // "Error" in a file name alone is not a problem: only a log with a real "!" error counts.
        LaTeXRenderService.ProblemsIn("(/texmf/tex/latex/ErrorHandling.sty)\nOutput written").Should().BeEmpty();
        LaTeXRenderService.ProblemsIn("! LaTeX Error: Missing \\begin{document}.\nl.2 e\nwcommand").Should()
            .Equal("! LaTeX Error: Missing \\begin{document}.", "l.2 e");
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task A_broken_preamble_line_gives_a_PDF_and_says_what_went_wrong()
    {
        const string latex = "\\documentclass{article}\newcommand{\\x}[1]{#1}\n\\begin{document}\nHello.\n\\end{document}\n";
        var (pdf, _, problems) = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfTolerantWithProblemsAsync(latex, 60, "pdflatex");
        pdf.Length.Should().BeGreaterThan(100, "LaTeX went past the error and made a PDF");
        problems.Should().NotBeEmpty();
        problems[0].Should().StartWith("! LaTeX Error: Missing \\begin{document}");
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task A_clean_document_has_none()
    {
        var (_, _, problems) = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance)
            .RenderToPdfTolerantWithProblemsAsync("\\documentclass{article}\n\\begin{document}\nHello.\n\\end{document}\n", 60, "pdflatex");
        problems.Should().BeEmpty();
    }
}
