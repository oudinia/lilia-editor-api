using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Xunit;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// A table the parser can't read as rows is kept as raw LaTeX, never dropped; a sidewaystable's content is read at
/// all; and the macros around it, three brace levels deep, are definitions, not text (the launch bot's TeX Stack
/// Exchange run, 14 Nov 2026, #105: "Could not parse table structure", an empty paragraph, and "\rpar[1]{…}" as text).
/// </summary>
public class LatexImportUnparsedTableTests
{
    private const string StepTable = """
        \documentclass{book}
        \usepackage{array}
        \usepackage[figuresright]{rotating}
        \usepackage{xltabular}

        \begin{document}
            \newlength{\pwid}
            \setlength{\pwid}{0.075\textwidth}
            \newcommand{\rot}[1]{\rotatebox[origin=br]{-45}{#1}}
            \newcommand{\rpar}[1]{\rot{\parbox[c][0.05\textwidth]{\pwid}{\raggedleft%
                    \leftskip 0pt plus \hsize minus \hsize %
                    #1}}}
            \newcolumntype{Q}{>{\tiny\arraybackslash}>{\raggedright}X}
            \renewcommand{\arraystretch}{1.3}
            \begin{sidewaystable}
                \begin{xltabular}{\textwidth}{|r |Q|Q|}
                    \multicolumn{3}{r}{\rpar{Amsterdam}} \\
                    \cline{3-3}
                    \multicolumn{2}{r|}{\rpar{Birmingham}} & \\
                \end{xltabular}
                \caption[Short]{A long caption.}
            \end{sidewaystable}
        \end{document}
        """;

    [Fact]
    public async Task The_unparsed_table_is_kept_whole_as_raw_LaTeX_and_no_definition_is_text()
    {
        var doc = await new LatexParser().ParseTextAsync(StepTable);

        var raw = doc.Elements.OfType<ImportLatexPassthrough>().Should().ContainSingle().Subject;
        raw.LatexCode.Should().StartWith(@"\begin{sidewaystable}").And.Contain(@"\begin{xltabular}").And.EndWith(@"\end{sidewaystable}");
        doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).Should().OnlyContain(t => string.IsNullOrWhiteSpace(t));
    }

    [Fact]
    public void The_macros_around_it_reach_the_preamble_whole()
    {
        LatexPreambleExtractor.Extract(StepTable).CustomPreamble!.Split('\n').Select(l => l.Trim()).Should().ContainInOrder(
            @"\newlength{\pwid}",
            @"\setlength{\pwid}{0.075\textwidth}",
            @"\newcommand{\rot}[1]{\rotatebox[origin=br]{-45}{#1}}",
            @"\newcommand{\rpar}[1]{\rot{\parbox[c][0.05\textwidth]{\pwid}{\raggedleft%",
            @"#1}}}",
            @"\newcolumntype{Q}{>{\tiny\arraybackslash}>{\raggedright}X}",
            @"\renewcommand{\arraystretch}{1.3}");
    }

    [Fact]
    public async Task A_sidewaystable_with_a_tabular_is_read_as_a_table()
    {
        var doc = await new LatexParser().ParseTextAsync("""
            \begin{sidewaystable}
            \begin{tabular}{ll}
            a & b \\
            c & d \\
            \end{tabular}
            \caption{Turned.}
            \end{sidewaystable}
            """);
        var table = doc.Elements.OfType<ImportTable>().Should().ContainSingle().Subject;
        table.Caption.Should().Be("Turned.");
        table.Rows.Should().HaveCount(2);
    }

    [Fact]
    public void Definitions_drawn_or_shown_as_written_are_left_alone()
    {
        const string source = """
            \begin{tikzpicture}
            \def\r{2}
            \draw (0,0) circle (\r);
            \end{tikzpicture}
            \begin{verbatim}
            \newcommand{\x}{y}
            \end{verbatim}
            """;
        LatexDefinitions.Find(source).Should().BeEmpty();
        LatexDefinitions.Strip(source).Should().Be(source);
    }

    [Fact]
    public void Def_reads_its_parameter_text()
    {
        LatexDefinitions.Strip(@"\def\pair#1#2{(#1,#2)}after").Should().Be("after");
        LatexDefinitions.Strip(@"\def\x{a{b{c}}}after").Should().Be("after");
    }
}
