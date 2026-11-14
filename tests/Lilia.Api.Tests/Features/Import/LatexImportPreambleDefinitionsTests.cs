using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// What a preamble defines reaches the document, and never its text (the launch bot's TeX Stack Exchange run,
/// 14 Nov 2026: #99, the preamble's xparse, expl3 and font declarations were lost; #102, <c>\colorlet{quack}{black}</c>
/// in the body printed "quack{black}"). The sources are the answers the bot pasted.
/// </summary>
public class LatexImportPreambleDefinitionsTests
{
    private const string Colorlet = """
        \documentclass{article}
        \usepackage{tabularray}
        \usepackage{xcolor}
        \begin{document}
        \colorlet{quack}{black}
        \begin{tblr}{hlines,vlines,cell{1}{2-3}={fg=quack}}
          one & two & {three\\four}
        \end{tblr}
        \end{document}
        """;

    private const string Expl3 = """
        \documentclass{article}
        \usepackage{amsmath,mathtools}
        \ExplSyntaxOn
        \NewDocumentCommand{\Matr}{O{b}m}
         {\duchamp_matrix:nn {#1} {#2}}
        \cs_new_protected:Nn \duchamp_matrix:nn
         {
          \begin{#1matrix}
          \seq_set_split:Nnn \l_tmpa_seq {;} {#2}
          \seq_map_function:NN \l_tmpa_seq \__duchamp_matrix_row:n
          \end{#1matrix}
         }
        \cs_new_protected:Nn \__duchamp_matrix_row:n
         {\clist_use:nn {#1} {&} \\
         }
        \ExplSyntaxOff
        \begin{document}
        \[
        \Matr[p]{1,2,3,4;5,6,7,5;4,3,2,1;0,1,0,1}
        \]
        \end{document}
        """;

    private const string Fonts = """
        \documentclass{article}
        \usepackage{amsmath}
        \usepackage{mathrsfs}

        % Declare Boondox font
        \DeclareSymbolFont{boondox}{U}{BOONDOX-cal}{m}{n}
        \DeclareMathSymbol{\boondoxS}{\mathalpha}{boondox}{83}
        % \definecolor{unused}{rgb}{1,0,0}

        \begin{document}
        $\boondoxS$
        \end{document}
        """;

    private const string Xparse = """
        \documentclass[12pt,a4paper]{amsart}
        \usepackage{xparse}
        \usepackage{hyperref}
        \newtheorem{exercise}{Esercizio}
        \NewDocumentEnvironment{Es}{o}
          {\begin{exercise}%
           \IfValueT{#1}{\label{ex:#1}}}
          {\IfValueT{#1}{%
             \par\nobreak\smallskip\noindent
             \hyperref[fig:#1]{Figura~\ref*{fig:#1}}%
           }%
           \end{exercise}}
        \NewDocumentEnvironment{Esfigure}{O{htbp} m m}
          {\begin{figure}[#1]\centering}
          {\caption{#3}\label{fig:#2}%
           \par\smallskip
           \hyperref[ex:#2]{Torna all'esercizio~\ref*{ex:#2}}%
           \end{figure}}
        \begin{document}
        Text.
        \end{document}
        """;

    [Fact]
    public async Task A_colour_defined_in_the_body_is_not_text_and_reaches_the_preamble()
    {
        var doc = await new LatexParser().ParseTextAsync(Colorlet);
        doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).Should().NotContain(t => t.Contains("quack"));
        LatexPreambleExtractor.Extract(Colorlet).CustomPreamble.Should().Be(@"\colorlet{quack}{black}");
    }

    [Fact]
    public void An_expl3_block_is_kept_whole()
    {
        var preamble = LatexPreambleExtractor.Extract(Expl3).CustomPreamble;
        preamble.Should().StartWith(@"\ExplSyntaxOn").And.EndWith(@"\ExplSyntaxOff");
        preamble.Should().Contain(@"\NewDocumentCommand{\Matr}{O{b}m}").And.Contain(@"\cs_new_protected:Nn \__duchamp_matrix_row:n");
    }

    [Fact]
    public void Font_and_symbol_declarations_are_kept_and_commented_ones_are_not()
    {
        LatexPreambleExtractor.Extract(Fonts).CustomPreamble!.Split('\n').Should().Equal(
            @"\DeclareSymbolFont{boondox}{U}{BOONDOX-cal}{m}{n}",
            @"\DeclareMathSymbol{\boondoxS}{\mathalpha}{boondox}{83}");
    }

    [Fact]
    public void Xparse_environments_are_kept_whole_at_any_depth()
    {
        var statements = LatexDefinitions.Statements(Xparse).Select(s => s.Text).ToList();
        statements.Should().HaveCount(2);
        statements[0].Should().StartWith(@"\NewDocumentEnvironment{Es}{o}").And.EndWith(@"\end{exercise}}");
        statements[1].Should().StartWith(@"\NewDocumentEnvironment{Esfigure}").And.EndWith(@"\end{figure}}");
    }

    [Fact]
    public void A_statement_reads_only_its_own_arguments()
    {
        LatexDefinitions.Strip("\\colorlet{a}{black}\nText {after}.\n\\definecolor{b}{rgb}{0,0,1} more")
            .Should().Be("\nText {after}.\n more");
        LatexDefinitions.Strip(@"\let\oldsection\section kept").Should().Be(" kept");
        LatexDefinitions.Strip(@"\colorlet{a}").Should().Be(@"\colorlet{a}");   // incomplete: left alone
    }

    [Fact]
    [Trait("Category", "RequiresTeX")]
    public async Task The_kept_expl3_preamble_compiles()
    {
        var latex = "\\documentclass{article}\n\\usepackage{amsmath,mathtools}\n"
            + LatexPreambleExtractor.Extract(Expl3).CustomPreamble
            + "\n\\begin{document}\n\\[\\Matr[p]{1,2;3,4}\\]\n\\end{document}\n";
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(latex, "pdflatex", timeout: 60);
        pdf.Length.Should().BeGreaterThan(100);
    }
}
