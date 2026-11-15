using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Features.Export;

/// <summary>
/// Two LaTeX failures the document sweep found (15 Nov 2026):
/// - 50 figures or tables in a row: "Output loop---100 consecutive dead cycles" and "Float(s) lost", 4 of 50 missing
///   from the PDF (E-007, E-043);
/// - an imported moderncv CV kept moderncv's \renewcommand*{\bibliographyitemlabel} in its custom preamble; Lilia prints
///   CVs on article, where the command doesn't exist, and the whole PDF failed (D-018).
/// </summary>
public class SweepPreambleTests
{
    [Fact]
    public void The_preamble_allows_enough_output_passes_for_many_floats()
        => LaTeXPreamble.Packages.Should().Contain(@"\maxdeadcycles=1000");

    [Fact]
    public void A_renewcommand_of_a_command_the_class_lacks_is_provided_first()
        => LaTeXPreamble.CustomPreambleForEmit(@"\renewcommand*{\bibliographyitemlabel}{[\arabic{enumiv}]}")
            .Should().Be(@"\providecommand{\bibliographyitemlabel}{}\renewcommand*{\bibliographyitemlabel}{[\arabic{enumiv}]}");

    [Fact]
    public void A_renewcommand_without_braces_is_provided_too()
        => LaTeXPreamble.CustomPreambleForEmit(@"\renewcommand\refname{Articles}")
            .Should().StartWith(@"\providecommand{\refname}{}\renewcommand\refname");

    [Fact]
    public void An_internal_at_command_is_left_alone()
        => LaTeXPreamble.CustomPreambleForEmit(@"\makeatletter\renewcommand{\@biblabel}[1]{#1.}\makeatother")
            .Should().Be(@"\makeatletter\renewcommand{\@biblabel}[1]{#1.}\makeatother");

    [Fact]
    public void A_preamble_without_renewcommand_is_unchanged()
        => LaTeXPreamble.CustomPreambleForEmit(@"\newcommand{\R}{\mathbb{R}}").Should().Be(@"\newcommand{\R}{\mathbb{R}}");
}
