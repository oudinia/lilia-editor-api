using FluentAssertions;
using Lilia.Api.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// A document with references built its bibliography (a pdflatex pass and BibTeX) before every PDF,
/// ahead of the compile cache: ~2.5 s of LTX-110's cached 3 s (18 Oct). The .bbl is now kept by the
/// project's exact files, and its pass starts from the precompiled preamble.
/// </summary>
public class BblCacheTests
{
    private static (string, string)[] Project(string marker, string extraCite = "") =>
    [
        ("main.tex", string.Join("\n",
            @"\documentclass{article}",
            @"\usepackage{amsmath}",
            @"\usepackage{natbib}",
            $@"\newcommand{{\liliabbl{marker}}}{{}}",
            "% Document info",
            @"\title{Bbl}",
            @"\begin{document}",
            $@"As \citet{{knuth84}} and \cite{{lamport94}} show{extraCite}.",
            @"\bibliographystyle{plainnat}",
            @"\bibliography{references}",
            @"\end{document}")),
        ("references.bib", """
            @book{knuth84, title={The TeXbook}, author={Knuth, Donald}, year={1984}, publisher={Addison-Wesley}}
            @book{lamport94, title={LaTeX}, author={Lamport, Leslie}, year={1994}, publisher={Addison-Wesley}}
            @book{goossens93, title={The LaTeX Companion}, author={Goossens, Michel}, year={1993}, publisher={Addison-Wesley}}
            """),
    ];

    [Fact]
    public async Task The_bibliography_is_the_same_with_the_preamble_format_and_is_kept()
    {
        var service = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var marker = new string(Guid.NewGuid().ToString("N").Select(c => (char)('a' + c % 26)).Take(8).ToArray());
        var project = Project(marker);

        var without = await service.GenerateBblAsync(project);
        without.Should().Contain("knuth84").And.Contain("lamport94").And.NotContain("goossens93");

        // With the document's format built, a changed text (so not the kept .bbl) gives the same bibliography.
        (await DocumentFormats.Shared!.EnsureAsync(project[0].Item2, "pdflatex", (d, e, p) => service.BuildFormatAsync(d, e, p), NullLogger.Instance))
            .Should().BeTrue();
        var edited = Project(marker, " too");
        (await service.GenerateBblAsync(edited)).Should().Be(without);

        // A new citation is a new project: it is in the bibliography.
        var cited = Project(marker, @" and \cite{goossens93}");
        (await service.GenerateBblAsync(cited)).Should().Contain("goossens93");

        // The same project again is answered from what was kept, at once.
        var sw = System.Diagnostics.Stopwatch.StartNew();
        (await service.GenerateBblAsync(cited)).Should().Contain("goossens93");
        sw.ElapsedMilliseconds.Should().BeLessThan(100);
    }
}
