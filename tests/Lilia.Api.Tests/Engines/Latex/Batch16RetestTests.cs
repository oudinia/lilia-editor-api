using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Engines.Latex;

/// <summary>
/// The launch bot's batch 15 retest (19 Nov 2026): \verb next to TeX's quotes and around a command, verbatim lines that
/// look like preamble (#222), natbib's other commands (#231), and a title's name and PDF title (#226).
/// </summary>
public class Batch16RetestTests
{
    private static Task<ImportDocument> Parse(string body) => new LatexParser().ParseTextAsync(
        "\\documentclass{article}\n\\begin{document}\n" + body + "\n\\end{document}\n");

    [Fact]
    public async Task Quoted_and_repeated_verb_keep_their_own_code_spans()
    {
        var doc = await Parse(@"After, with ``\verb|acmart|'' quoted and {\verb|documentclass|} braced, and the {\itshape template style} in italics.
Use \verb|\eqref{Eq}| and \verb+\cite{#1}+ as typed.");
        var latex = LaTeXExportService.FormatInlineContent(doc.Elements.OfType<ImportParagraph>().Single().Text);

        latex.Should().Contain(@"\texttt{acmart}").And.Contain(@"\texttt{documentclass}");
        latex.Should().Contain(@"\texttt{\textbackslash{}eqref\{Eq\}}").And.Contain(@"\texttt{\textbackslash{}cite\{\#1\}}");
        latex.Should().NotContain(@"\eqref{").And.NotContain(@"\cite{").And.NotContain("*template style*");
    }

    [Fact]
    public void A_command_inside_code_is_printed_not_run()
    {
        LaTeXExportService.FormatInlineContent("Use `\\cite{key}` here.").Should().Be(@"Use \texttt{\textbackslash{}cite\{key\}} here.");
    }

    [Fact]
    public async Task Verbatim_lines_that_look_like_preamble_stay()
    {
        var doc = await Parse(@"Text.
\begin{verbatim}
  \documentclass[STYLE]{acmart}
\title[short title]{full title}
\renewcommand{\shortauthors}{Trovato et al.}
\bibliographystyle{ACM-Reference-Format}
plain text line
\end{verbatim}
More.");
        var code = doc.Elements.OfType<ImportCodeBlock>().Single().Text;
        code.Should().Contain(@"  \documentclass[STYLE]{acmart}")
            .And.Contain(@"\title[short title]{full title}")
            .And.Contain(@"\renewcommand{\shortauthors}{Trovato et al.}")
            .And.Contain(@"\bibliographystyle{ACM-Reference-Format}")
            .And.Contain("plain text line");
    }

    [Theory]
    [InlineData(@"\citenum{lamport}", true)]
    [InlineData(@"\citealp{knuth}", true)]
    [InlineData(@"\citealt{knuth}", true)]
    [InlineData(@"\Citet{knuth}", true)]
    [InlineData(@"\cite{knuth}", false)]
    public void Natbib_is_loaded_for_its_other_commands(string text, bool loads) =>
        LaTeXPreamble.UsesNatbib([text]).Should().Be(loads);

    [Theory]
    [InlineData(@"Conference Paper Title*\\ {\footnotesize \textsuperscript{*}Note: Sub-titles} \thanks{Funding.}", "Conference Paper Title* *Note: Sub-titles")]
    [InlineData(@"Sample Title:\\with Forced Linebreak\footnote{Error!}", "Sample Title: with Forced Linebreak")]
    [InlineData(@"Residual-Guided Learning for\\Risk-Aware Selection", "Residual-Guided Learning for Risk-Aware Selection")]
    [InlineData(@"Designing with \LaTeX{} \& \textit{friends}", "Designing with LaTeX & friends")]
    public void A_title_as_plain_text(string latex, string plain) =>
        LatexText.Plain(latex).Should().Be(plain);

    [Fact]
    public async Task A_subtitle_group_loses_its_braces_and_keeps_its_superscript()
    {
        var doc = await new LatexParser().ParseTextAsync(@"\documentclass{article}
\title{Conference Paper Title*\\ {\footnotesize \textsuperscript{*}Note: Sub-titles}}
\begin{document}\maketitle Body.\end{document}");
        doc.Title.Should().Contain(@"\textsuperscript{*}Note: Sub-titles").And.NotContain("{\\footnotesize").And.NotContain("{\\textsuperscript");
        LaTeXExportService.FormatTitleMetaLatex(doc.Title!).Should().Contain(@"\textsuperscript{*}Note").And.NotContain(@"\{");
    }

    [Fact]
    public void The_PDF_title_is_plain_text()
    {
        var doc = new Lilia.Core.Entities.Document { Title = "x" };
        RenderService.PdfProperties(doc, @"Learning for\\Risk-Aware \thanks{Funding.}")
            .Should().Contain("pdftitle={Learning for Risk-Aware}");
    }
}
