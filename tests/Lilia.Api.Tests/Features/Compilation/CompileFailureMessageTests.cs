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

    [Fact]
    public void Each_error_comes_with_the_source_line_LaTeX_names()
    {
        // A missing package in the custom preamble (the launch bot's TeX Stack Exchange run, 14 Nov 2026).
        const string log = "(./document.tex\n! LaTeX Error: File `isodate.sty' not found.\n\nType X to quit or <RETURN> to proceed,\n"
            + "or enter new name. (Default extension: sty)\n\nEnter file name: \n! Emergency stop.\n<read *> \n         \n"
            + "l.12 \\usepackage{isodate}\n                          ^^M\n";
        LaTeXRenderService.CompileErrorLines(log).Should().Equal(
            "! LaTeX Error: File `isodate.sty' not found.",
            "! Emergency stop.",
            "l.12 \\usepackage{isodate}");
    }

    [Fact]
    public void An_undefined_command_names_its_line()
    {
        const string log = "! Undefined control sequence.\nl.3 Hello \\undefinedcommandxyz\n                         \n";
        LaTeXRenderService.CompileErrorLines(log).Should().Equal("! Undefined control sequence.", "l.3 Hello \\undefinedcommandxyz");
    }
}
