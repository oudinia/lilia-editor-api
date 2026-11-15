using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// The launch bot's #100 retest (15 Nov 2026): the error panel said "l.401", a line of the generated .tex the author
/// never sees. A line in the author's Custom preamble now says so and quotes it.
/// </summary>
public class CompileErrorWhereTests
{
    private static string Source(params string[] preamble) => string.Join("\n",
        new[] { @"\documentclass{article}", @"\usepackage{amsmath}", RenderService.YourPreambleStart }
            .Concat(preamble)
            .Concat([RenderService.YourPreambleEnd, @"\begin{document}", "Hello.", @"\end{document}"]));

    // As pdflatex writes it: a missing package is reported at the line where LaTeX stopped reading, past the preamble.
    private const string MissingPackageLog = "(./document.tex\n! LaTeX Error: File `doesnotexist.sty' not found.\n\n"
        + "Type X to quit or <RETURN> to proceed,\n! Emergency stop.\n<read *> \n\nl.7 \\begin\n";

    private const string UndefinedLog = "(./document.tex\n! Undefined control sequence.\nl.5 \\foo\n";

    [Fact]
    public void A_missing_package_quotes_the_author_s_line_that_loads_it()
    {
        var lines = LaTeXRenderService.CompileErrorLines(MissingPackageLog,
            Source(@"\usepackage{xcolor}", @"\usepackage[draft]{tikz,doesnotexist}"));

        lines.Should().Contain(l => l.StartsWith("l.7") && l.EndsWith(@"← your Custom preamble: \usepackage[draft]{tikz,doesnotexist}"));
    }

    [Fact]
    public void Another_error_in_the_Custom_preamble_quotes_the_line_LaTeX_names()
    {
        // Line 5: \documentclass, amsmath, the start marker, then the author's two lines.
        var lines = LaTeXRenderService.CompileErrorLines(UndefinedLog, Source(@"\usepackage{xcolor}", @"\foo"));

        lines.Should().Contain(l => l.StartsWith("l.5") && l.EndsWith(@"← your Custom preamble: \foo"));
    }

    [Fact]
    public void A_package_the_author_did_not_load_is_not_pinned_on_them()
        => LaTeXRenderService.CompileErrorLines(MissingPackageLog, Source(@"\usepackage{xcolor}"))
            .Should().NotContain(l => l.Contains("Custom preamble"));

    [Fact]
    public void Lines_outside_the_Custom_preamble_stay_as_LaTeX_wrote_them()
    {
        var source = Source(@"\usepackage{xcolor}");
        LaTeXRenderService.InYourPreamble(source.Split('\n'), 1).Should().BeNull("Lilia's own preamble");
        LaTeXRenderService.InYourPreamble(source.Split('\n'), 3).Should().BeNull("the marker itself");
        LaTeXRenderService.InYourPreamble(source.Split('\n'), 4).Should().Be(@"\usepackage{xcolor}");
        LaTeXRenderService.InYourPreamble(source.Split('\n'), 5).Should().BeNull("the end marker");
        LaTeXRenderService.InYourPreamble(source.Split('\n'), 7).Should().BeNull("the body");
        LaTeXRenderService.InYourPreamble(source.Split('\n'), 99).Should().BeNull();
    }

    [Fact]
    public void Without_the_source_or_its_markers_nothing_is_added()
    {
        LaTeXRenderService.CompileErrorLines(MissingPackageLog).Should().Contain(@"l.7 \begin");
        LaTeXRenderService.CompileErrorLines(MissingPackageLog, "\\documentclass{article}\n\\begin{document}\nx\n\\end{document}")
            .Should().Contain(@"l.7 \begin");
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task A_real_compile_names_the_author_s_line()
    {
        var latex = Source(@"\usepackage{doesnotexist}");
        var act = () => new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(latex, "pdflatex", timeout: 60);

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("doesnotexist.sty").And.Contain(@"your Custom preamble: \usepackage{doesnotexist}");
    }
}
