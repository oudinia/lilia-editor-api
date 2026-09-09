using FluentAssertions;
using Lilia.Api.Services;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// The Validate panel reported "There were undefined citations" for a document
/// whose bibliography was complete and whose exported PDF resolved every
/// citation correctly (2026-09-09).
///
/// <para>Validation compiles a single pdflatex pass, and citations resolve
/// through the .aux file that pass writes — so on that pass every citation is
/// undefined regardless of whether anything is wrong. The warning was pure
/// noise. What must survive is the real check, which does not come from the
/// compiler at all: ValidateDocument compares cite keys against the
/// bibliography entries directly.</para>
/// </summary>
public class SinglePassWarningTests
{
    [Theory]
    [InlineData(@"Package natbib Warning: Citation `historyref' on page 2 undefined on input line 40.")]
    [InlineData(@"LaTeX Warning: Citation `euclid_elements' on page 1 undefined on input line 12.")]
    [InlineData("Package natbib Warning: There were undefined citations.")]
    [InlineData(@"LaTeX Warning: Reference `sec:intro' on page 1 undefined on input line 8.")]
    [InlineData("LaTeX Warning: There were undefined references.")]
    [InlineData("Package natbib Warning: Citation(s) may have changed.")]
    public void SinglePassArtifactsAreRecognised(string warning) =>
        LaTeXRenderService.IsSinglePassCitationArtifact(warning).Should().BeTrue();

    [Theory]
    [InlineData(@"Overfull \vbox (12.0pt too high) has occurred while \output is active")]
    [InlineData("LaTeX Warning: Float too large for page by 1161.16pt on input line 300.")]
    [InlineData("Package hyperref Warning: Token not allowed in a PDF string.")]
    [InlineData(@"LaTeX Font Warning: Font shape `OT1/cmr/bx/sc' undefined")]
    [InlineData("Missing bibliography entry: \\cite{historyref}")]
    public void RealWarningsAreNot(string warning) =>
        LaTeXRenderService.IsSinglePassCitationArtifact(warning).Should().BeFalse();

    [Theory]
    [InlineData("Package epstopdf Warning: Shell escape feature is not enabled.")]
    public void TheEnvironmentNoticeIsRecognised(string warning) =>
        LaTeXRenderService.IsEnvironmentNotice(warning).Should().BeTrue();

    [Theory]
    [InlineData("Package epstopdf Warning: Could not convert `fig.eps'.")]
    [InlineData("Package hyperref Warning: Shell escape feature is not enabled.")]
    public void OtherEpstopdfAndShellEscapeMessagesAreNot(string warning) =>
        LaTeXRenderService.IsEnvironmentNotice(warning).Should().BeFalse(
            "only the load-time notice is unconditional; anything naming a real "
            + "figure is about this document");

    /// <summary>
    /// The EPS error names a file the author never wrote: pdftex.def reports
    /// `fig-eps-converted-to.pdf`, which is what the conversion would have
    /// produced. The author wrote fig.eps.
    /// </summary>
    [Fact]
    public void TheEpsErrorNamesTheFigureTheAuthorActuallyWrote()
    {
        var raw = "! Package pdftex.def Error: File `fig-eps-converted-to.pdf' "
                + "not found: using draft setting.";
        var friendly = LaTeXRenderService.HumaniseKnownErrors(raw);

        friendly.Should().Contain("fig.eps");
        friendly.Should().NotContain("-eps-converted-to");
        friendly.Should().MatchRegex("(?i)pdf or png", "the author needs to know what to do instead");
    }

    [Fact]
    public void OtherErrorsPassThroughUntouched()
    {
        const string raw = "! Undefined control sequence.\nl.15 \\citep";
        LaTeXRenderService.HumaniseKnownErrors(raw).Should().Be(raw);
    }

    [Fact]
    public void HumanisingIsSafeOnEmptyInput() =>
        LaTeXRenderService.HumaniseKnownErrors("").Should().Be("");

    /// <summary>
    /// The one that decides this is safe: a genuinely missing key is still
    /// reported, because that check compares blocks against entries rather than
    /// reading the compiler log.
    /// </summary>
    [Fact]
    public void TheMissingEntryWarningSurvivesTheFilter() =>
        LaTeXRenderService.IsSinglePassCitationArtifact(
            @"Missing bibliography entry: \cite{euclid_elements}").Should().BeFalse();
}
