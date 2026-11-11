using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// What a failed compile says (the launch bot's #51, 11 Nov 2026). A compile stopped from outside (the server restarting
/// mid-compile) left a bare "LaTeX compilation failed:"; and a failed first pass was followed by another that read its
/// half-written .aux and reported that instead ("File ended while scanning use of \BKM@entry").
/// </summary>
public class CompileFailureMessageTests
{
    [Fact]
    public void A_log_with_no_latex_error_says_the_compile_was_interrupted()
    {
        LaTeXRenderService.CompileErrorLines("This is LuaHBTeX, Version 1.22\n(./document.tex\n").Should().BeEmpty();
        LaTeXRenderService.InterruptedMessage.Should().Contain("interrupted").And.Contain("Try again");
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task A_broken_document_reports_its_own_error_not_a_later_pass_s()
    {
        var latex = "\\documentclass{article}\n\\begin{document}\nHello \\undefinedcommandxyz\n\\end{document}\n";
        var act = () => new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(latex, "pdflatex", timeout: 60);
        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Undefined control sequence").And.NotContain("interrupted");
    }
}
