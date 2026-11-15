using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// The document sweep (E-033, 15 Nov 2026): 300 headings in a row printed 31 in the LaTeX PDF, with no error. LaTeX
/// keeps each heading with what follows, the run became one block taller than a page, and the rest went off the
/// page. Its log says so ("Overfull \vbox … while \output is active"); the preview now says some text is cut off.
/// </summary>
public class TextCutOffProblemTests
{
    private const string CutOffLog = """
        Package newunicodechar Warning: Redefining Unicode character on input line 159.
        Overfull \vbox (544.13448pt too high) has occurred while \output is active
        [3]
        Output written on main.pdf (3 pages, 95509 bytes).
        """;

    [Fact]
    public void Text_pushed_off_the_page_is_a_problem_the_preview_counts()
    {
        var problems = LaTeXRenderService.ProblemsIn(CutOffLog);
        problems.Should().ContainSingle().Which.Should().StartWith("!").And.Contain("cut off").And.Contain("544pt");
    }

    [Fact]
    public void A_few_points_of_overflow_lose_nothing_and_say_nothing()
        => LaTeXRenderService.ProblemsIn("Overfull \\vbox (2.5pt too high) has occurred while \\output is active\nOutput written on main.pdf (1 page).")
            .Should().BeEmpty();

    [Fact]
    public void A_clean_log_has_no_problems()
        => LaTeXRenderService.ProblemsIn("Output written on main.pdf (1 page, 1000 bytes).").Should().BeEmpty();

    [Fact]
    public void An_error_and_a_cut_off_are_both_reported()
    {
        var log = "! Undefined control sequence.\nl.12 \\foo\n" + CutOffLog;
        var problems = LaTeXRenderService.ProblemsIn(log);
        problems.Should().Contain(p => p.StartsWith("! Undefined control sequence"));
        problems.Should().Contain(p => p.StartsWith(LaTeXRenderService.TextCutOffProblem));
    }
}
