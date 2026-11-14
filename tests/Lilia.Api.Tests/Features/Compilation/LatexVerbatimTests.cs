using FluentAssertions;
using Lilia.Engines;
using Xunit;

namespace Lilia.Api.Tests.Features.Compilation;

/// <summary>
/// Text a code block only shows is not the document's own LaTeX (MCP-127, MCP-128; retested
/// 14 Nov): it chose the engine and counted as the document's labels and references.
/// </summary>
public class LatexVerbatimTests
{
    private const string Doc = """
        \documentclass{article}
        \begin{document}
        \begin{equation}\label{eq:smoke}
        E = mc^2
        \end{equation}
        See \ref{eq:smoke}.
        % block:11111111-1111-1111-1111-111111111111
        \begin{lstlisting}[language=TeX, label={lst:shown}]
        \usepackage{xeCJK}
        \setmainfont{Noto Serif}
        \begin{equation}\label{eq:smoke}
        See \ref{eq:missing}.
        \end{lstlisting}
        \end{document}
        """;

    [Fact]
    public void Blanks_the_body_and_keeps_the_lines()
    {
        var blanked = LatexVerbatim.Blank(Doc);
        blanked.Split('\n').Should().HaveSameCount(Doc.Split('\n'), "line numbers still point at the same blocks");
        blanked.Should().NotContain("xeCJK").And.NotContain("eq:missing")
            .And.Contain(@"label={lst:shown}", "a listing's label is in its options, on the begin line")
            .And.Contain(@"\end{lstlisting}").And.Contain(@"See \ref{eq:smoke}.");
    }

    [Fact]
    public void A_code_block_does_not_choose_the_engine()
    {
        EngineDetector.Detect(Doc).Should().Be(LatexEngine.Pdflatex);
        EngineDetector.RequiresLua(Doc.Replace(@"\usepackage{xeCJK}", @"\directlua{x=1}")).Should().BeFalse();
        // The same command outside a code block still does.
        EngineDetector.Detect(Doc.Replace(@"\begin{document}", "\\usepackage{xeCJK}\n\\begin{document}")).Should().Be(LatexEngine.Xelatex);
    }

    [Fact]
    public void A_code_block_s_labels_and_references_are_not_the_document_s()
    {
        var audit = new EmittedLatexAudit(Doc);
        audit.DuplicateLabels.Should().BeEmpty();
        audit.DanglingReferences.Should().BeEmpty();
    }

    [Fact]
    public void A_one_line_environment_closes_on_its_own_line()
    {
        var tex = "\\begin{verbatim}\\ref{a}\\end{verbatim}\nSee \\ref{b}.\n";
        LatexVerbatim.Blank(tex).Should().Be(tex);
    }
}
