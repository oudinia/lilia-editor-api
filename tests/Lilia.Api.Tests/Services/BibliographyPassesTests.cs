using System.Diagnostics;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// BibTeX inside the PDF compile (pass 1 → BibTeX → pass 2) instead of a compile of its own before
/// it: the same citations and bibliography, one TeX run fewer (LTX-110, 18 Oct).
/// </summary>
public class BibliographyPassesTests
{
    [Fact]
    public void A_label_using_a_command_the_bbl_defines_is_left_to_the_third_pass_and_protecting_braces_are_read_through()
    {
        // REVTeX (apsrev4-2): the label's \citenamefont exists only once the .bbl is read, after the .aux.
        var revtex = "\\providecommand \\citenamefont [1]{#1}%\n\\bibitem [{\\citenamefont {Knuth}(1984)}]{knuth84}%\n";
        BibliographyPasses.Bibcites(revtex, natbib: true).Should().BeEmpty();

        // [{label}] without such a command: natbib reads inside the braces.
        BibliographyPasses.Bibcites("\\bibitem[{Knuth(1984)Knuth, D.}]{knuth84} x", natbib: true)
            .Should().Be("\\bibcite{knuth84}{{1}{1984}{{Knuth}}{{Knuth, D.}}}\n");
    }

    [Fact]
    public void Numbered_items_cite_by_their_order_and_labelled_ones_by_their_label()
    {
        var bbl = "\\begin{thebibliography}{2}\n\n\\bibitem{knuth84}\nD. Knuth.\n\n\\bibitem[Lam94]{lamport94}\nL. Lamport.\n\n\\bibitem{goossens93}\nM. Goossens.\n\\end{thebibliography}\n";
        BibliographyPasses.Bibcites(bbl, natbib: false).Should().Be(
            "\\bibcite{knuth84}{1}\n\\bibcite{lamport94}{Lam94}\n\\bibcite{goossens93}{2}\n");
    }

    [Fact]
    public void Natbib_items_carry_number_year_short_and_long_names_even_across_lines()
    {
        var bbl = "\\bibitem[Knuth(1984)]{knuth84}\nD. Knuth.\n\n\\bibitem[Goossens et~al.(1993)Goossens, Mittelbach, and\n  Samarin]{goossens93}\nM. Goossens.\n";
        BibliographyPasses.Bibcites(bbl, natbib: true).Should().Be(
            "\\bibcite{knuth84}{{1}{1984}{{Knuth}}{{}}}\n"
            + "\\bibcite{goossens93}{{2}{1993}{{Goossens et~al.}}{{Goossens, Mittelbach, and Samarin}}}\n");
    }

    [Fact]
    public void Natbib_is_seen_in_the_source_or_the_log()
    {
        BibliographyPasses.UsesNatbib(@"\usepackage[numbers,sort]{natbib}", "").Should().BeTrue();
        BibliographyPasses.UsesNatbib(@"\usepackage{amsmath,natbib}", "").Should().BeTrue("the preamble format's log lists no packages");
        BibliographyPasses.UsesNatbib(@"\documentclass{elsarticle}", "Package: natbib 2010/09/13 8.31b").Should().BeTrue();
        BibliographyPasses.UsesNatbib(@"\usepackage{natbibx}", "").Should().BeFalse();
    }

    [Fact]
    public void Only_a_settled_pass_is_the_last()
    {
        BibliographyPasses.NeedsRerun("LaTeX Warning: Label(s) may have changed. Rerun to get cross-references right.\n").Should().BeTrue();
        BibliographyPasses.NeedsRerun("Package natbib Warning: Citation(s) may have changed.\n(natbib)                Rerun to get citations correct.\n").Should().BeTrue();
        BibliographyPasses.NeedsRerun("Package: rerunfilecheck 2025-06-21 v1.11 Rerun checks for auxiliary files (HO)\nOutput written on document.pdf\n").Should().BeFalse();
    }

    [Fact]
    public void BibTeX_runs_only_for_a_database_that_is_there()
    {
        var dir = Directory.CreateTempSubdirectory("lilia-bibpasses-").FullName;
        try
        {
            BibliographyPasses.HasDatabase("\\bibdata{references}", dir).Should().BeFalse("no .bib beside the .tex");
            File.WriteAllText(Path.Combine(dir, "references.bib"), "");
            BibliographyPasses.HasDatabase("\\bibdata{references}", dir).Should().BeFalse("an empty .bib");
            File.WriteAllText(Path.Combine(dir, "references.bib"), "@book{a, title={A}}");
            BibliographyPasses.HasDatabase("\\bibdata{other,references}", dir).Should().BeTrue();
            BibliographyPasses.HasDatabase("\\bibdata{../references}", dir).Should().BeFalse("only what is beside the .tex");
            BibliographyPasses.HasDatabase("\\relax", dir).Should().BeFalse("no bibliography asked for");
        }
        finally { Directory.Delete(dir, true); }
    }

    private const string Bib = """
        @book{knuth84, title={The TeXbook}, author={Knuth, Donald}, year={1984}, publisher={Addison-Wesley}}
        @book{lamport94, title={LaTeX: A Document Preparation System}, author={Lamport, Leslie}, year={1994}, publisher={Addison-Wesley}}
        @book{goossens93, title={The LaTeX Companion}, author={Goossens, Michel and Mittelbach, Frank and Samarin, Alexander}, year={1993}, publisher={Addison-Wesley}}
        @book{unused, title={Never Cited}, author={Nobody, A.}, year={2000}, publisher={None}}
        """;

    private static string Document(string packages, string style, string marker) => string.Join("\n",
        @"\documentclass{article}",
        packages,
        @"\usepackage{hyperref}",
        @"\begin{document}",
        $@"\section{{Intro}}\label{{sec:intro}} {marker}",
        @"As \cite{knuth84} and \cite{lamport94} show, and \cite{goossens93} too; see Section~\ref{sec:intro}.",
        $@"\bibliographystyle{{{style}}}",
        @"\bibliography{references}",
        @"\end{document}");

    public static TheoryData<string, string> Styles => new()
    {
        { "", "plain" },
        { "", "unsrt" },
        { @"\usepackage{natbib}", "plainnat" },
    };

    [Theory]
    [MemberData(nameof(Styles))]
    public async Task The_PDF_prints_what_the_inlined_bibliography_printed_in_two_passes(string packages, string style)
    {
        var service = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var marker = $"Run {Guid.NewGuid():N}.";
        var tex = Document(packages, style, marker);
        Task WriteBib(string dir) => File.WriteAllTextAsync(Path.Combine(dir, "references.bib"), Bib);

        // As the PDF was made before: the .bbl built on its own, pasted in place of \bibliography.
        var bbl = await service.GenerateBblAsync([("main.tex", tex), ("references.bib", Bib)]);
        bbl.Should().NotBeNullOrWhiteSpace();
        var before = Text(await service.RenderToPdfTolerantAsync(tex.Replace(@"\bibliography{references}", bbl!), materialiseAssets: WriteBib));

        var timing = Timing.Begin();
        var after = Text(await service.RenderToPdfTolerantAsync(tex, materialiseAssets: WriteBib));

        after.Should().Be(before);
        after.Should().Contain("The TeXbook").And.Contain("Samarin").And.NotContain("Never Cited").And.NotContain("[?]").And.NotContain("??");
        timing.Steps().Should().Contain("tex.bibtex").And.NotContain("tex.pass 3", "the citations were in the .aux before pass 2");
    }

    [Fact]
    public async Task Without_a_bib_beside_it_the_document_compiles_as_before()
    {
        var service = new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance);
        var timing = Timing.Begin();
        var text = Text(await service.RenderToPdfTolerantAsync(Document("", "plain", $"Run {Guid.NewGuid():N}.")));
        text.Should().Contain("[?]").And.NotContain("References");
        timing.Steps().Should().NotContain("tex.bibtex");
    }

    private static string Text(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-bibpasses-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            using var p = Process.Start(new ProcessStartInfo("pdftotext", $"-layout {path} -") { RedirectStandardOutput = true })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text;
        }
        finally { File.Delete(path); }
    }
}
