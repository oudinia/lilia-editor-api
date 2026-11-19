using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Engines.Publishers;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's #243 (19 Nov 2026): the AMS proceedings sample (proc-l) didn't compile. \specialsection*{…} lost its
/// braces, $E\cup⏎F$ across a line break was escaped, and \address, \email, \subjclass, \dedicatory and \keywords printed
/// as body text, because proc-l isn't in TeX Live and the document fell back to article.
/// </summary>
public class Batch27AmsProcTests
{
    private const string Sample = @"\documentclass{proc-l}
\begin{document}
\title{Sample paper}
\author{Jane Doe}
\address{Department of Mathematics, Louisiana State University}
\email{jane@example.edu}
\subjclass[2000]{Primary 54C40, 14E20; Secondary 46E25, 20C20}
\dedicatory{This paper is dedicated to our advisors.}
\keywords{Differential geometry, algebraic geometry}
\maketitle
\specialsection*{THIS IS A SPECIAL SECTION HEAD}
This is an example of a special section head, then $f$ is $E\cup
F$-regular.
\end{document}";

    [Fact]
    public async Task The_ams_proceedings_sample_reads_as_amsart()
    {
        var doc = await new LatexParser().ParseTextAsync(Sample);
        doc.Metadata.DocumentClass.Should().Be("amsart");
        var heading = doc.Elements.OfType<ImportHeading>().Single();
        heading.Text.Should().Be("THIS IS A SPECIAL SECTION HEAD");
        var body = string.Join("\n", doc.Elements.OfType<ImportParagraph>().Select(p => p.Text));
        body.Should().Contain("This is an example of a special section head").And.NotContain("Louisiana")
            .And.NotContain("Primary 54C40").And.NotContain("dedicated").And.NotContain("Differential geometry");
        doc.Metadata.FrontExtra.Should().Be(
            "\\subjclass[2000]{Primary 54C40, 14E20; Secondary 46E25, 20C20}\n\\dedicatory{This paper is dedicated to our advisors.}");
    }

    [Fact]
    public void Amsart_writes_the_subjclass_and_dedicatory()
    {
        var (pre, _) = PublisherClasses.FrontMatter("amsart", "Sample paper", @"Jane Doe\\Department of Mathematics", null, ["Differential geometry"],
            s => s, "Sample paper", frontExtra: "\\subjclass[2000]{Primary 54C40}\n\\dedicatory{To our advisors.}\n\\input{evil}");
        pre.Should().Contain("\\subjclass[2000]{Primary 54C40}\n").And.Contain("\\dedicatory{To our advisors.}\n").And.NotContain("evil");
    }

    [Fact]
    public void Inline_maths_across_one_line_break_stays_maths()
    {
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance)
            .RenderBlockToLatex(B("paragraph", new { text = "then $f$ is $E\\cup\nF$-regular.\n\nA new paragraph with $x$." }));
        latex.Should().Contain("$E\\cup\nF$-regular").And.NotContain(@"\$");
    }
}
