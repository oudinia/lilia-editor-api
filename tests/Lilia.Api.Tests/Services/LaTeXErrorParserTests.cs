using FluentAssertions;
using Lilia.Api.Controllers;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The phone's Check document taps a compile error through to its block, from the
/// "l.N" line TeX writes. A whole document's log opens with pages of package
/// loading, so the error sat beyond the parser's first-2000-characters window and
/// came back with no line (10 Oct 2026).
/// </summary>
public class LaTeXErrorParserTests
{
    private static string LongLog(string error) =>
        string.Concat(Enumerable.Repeat("(/usr/share/texlive/texmf-dist/tex/latex/base/size11.clo File: size11.clo)\n", 80))
        + error;

    [Fact]
    public void The_line_is_found_past_a_long_preamble()
    {
        var log = LongLog("! Undefined control sequence.\nl.322 \\badmacro\n                x\n");
        log.Length.Should().BeGreaterThan(2000);
        var parsed = LaTeXErrorParser.Parse(log);
        parsed!.LineNumber.Should().Be(322);
        parsed.Category.Should().Be("undefined_control_sequence");
    }

    [Fact]
    public void A_short_log_that_starts_with_the_error_still_parses()
    {
        LaTeXErrorParser.Parse("! Undefined control sequence.\nl.15 \\missingcmd\n")!.LineNumber.Should().Be(15);
    }

    [Fact]
    public void The_error_line_drops_the_wrapper_and_the_fatal_footer()
    {
        LaTeXRenderController.FirstErrorLine("LaTeX compilation failed:\n! Undefined control sequence.\n!  ==> Fatal error occurred, no output PDF file produced!")
            .Should().Be("Undefined control sequence.");
        LaTeXRenderController.FirstErrorLine(null).Should().Be("The document does not compile.");
        LaTeXRenderController.FirstErrorLine("Validation service unavailable").Should().Be("Validation service unavailable");
    }
}
