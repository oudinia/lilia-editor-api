using FluentAssertions;
using Lilia.Api.Services;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// Launch-bot #34 (13 Nov 2026): \frac{a} was completed to \frac{a}{}, which compiles and prints a blank, and
/// the check said all was well. The check now warns, on the line (so the block) that has it.
/// </summary>
public class EmptyFractionCheckTests
{
    [Fact]
    public void An_empty_part_in_a_fraction_is_found_on_its_line()
    {
        var source = "\\begin{document}\n$x = \\frac{a}{}$\n\\[ \\dfrac{}{b} \\]\n$\\frac{a}{b}$\n\\end{document}";
        DocumentCheckService.EmptyFractionLines(source).Should().Equal(2, 3);
    }

    [Theory]
    [InlineData(@"\frac{a}{b}")]
    [InlineData(@"\frac{\alpha + 1}{2}")]
    [InlineData(@"\tfrac12")]
    public void A_complete_fraction_is_left_alone(string latex)
    {
        DocumentCheckService.EmptyFractionLines(latex).Should().BeEmpty();
    }
}
