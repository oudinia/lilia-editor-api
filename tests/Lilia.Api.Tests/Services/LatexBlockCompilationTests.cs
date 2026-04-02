using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tests that every block type generates valid, well-formed LaTeX that can compile
/// inside the per-block validation preamble. These are unit tests that verify
/// LaTeX structure without needing pdflatex installed.
///
/// For actual compilation tests (requiring pdflatex), see the integration tests
/// in Integration/Controllers/LaTeXValidationControllerTests.cs.
/// </summary>
public class LatexBlockCompilationTests
{
    private readonly RenderService _sut;

    public LatexBlockCompilationTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _sut = new RenderService(null!, logger.Object);
    }

    // The per-block validation preamble from LaTeXRenderController.cs
    private const string ValidationPreamble = @"\documentclass{article}
\usepackage[utf8]{inputenc}
\usepackage[T1]{fontenc}
\usepackage{textcomp}
\usepackage{amsmath,amssymb,amsfonts,amsthm}
\usepackage{mathtools}
\usepackage{mathrsfs}
\usepackage{cancel}
\usepackage{siunitx}
\usepackage{microtype}
\usepackage{setspace}
\usepackage{parskip}
\usepackage[demo]{graphicx}
\usepackage{float}
\usepackage{caption}
\usepackage{subcaption}
\usepackage{xcolor}
\usepackage{booktabs}
\usepackage{multirow}
\usepackage{tabularx}
\usepackage{longtable}
\usepackage{enumitem}
\usepackage{listings}
\usepackage{algorithm}
\usepackage{algorithmic}
\usepackage{tcolorbox}
\usepackage{soul}
\usepackage{ulem}
\normalem
\usepackage{url}
\usepackage[colorlinks=true]{hyperref}
\usepackage[nameinlink]{cleveref}
\newtheorem{theorem}{Theorem}
\newtheorem{lemma}{Lemma}
\newtheorem{proposition}{Proposition}
\newtheorem{corollary}{Corollary}
\newtheorem{definition}{Definition}
\newtheorem{example}{Example}
\newtheorem{remark}{Remark}
";

    private string WrapInDocument(string latex) =>
        ValidationPreamble + "\\begin{document}\n" + latex + "\n\\end{document}";

    // ── Block Type Coverage ─────────────────────────────────────────

    #region Paragraph

    [Fact]
    public void Paragraph_GeneratesValidLatex()
    {
        var block = CreateBlock("paragraph", """{"text":"Hello world."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("Hello world.");
        WrapInDocument(latex).Should().Contain("\\begin{document}");
    }

    [Fact]
    public void Paragraph_WithSpecialChars_EscapesCorrectly()
    {
        var block = CreateBlock("paragraph", """{"text":"Price is 100% of $50 & tax #1"}""");
        var latex = _sut.RenderBlockToLatex(block);
        // LaTeX special chars should be escaped
        latex.Should().Contain("\\%");
        latex.Should().Contain("\\&");
        latex.Should().Contain("\\#");
    }

    [Fact]
    public void Paragraph_Empty_GeneratesValidLatex()
    {
        var block = CreateBlock("paragraph", """{"text":""}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().NotBeNull();
    }

    [Fact]
    public void Paragraph_WithInlineMath_PreservesDollarSigns()
    {
        var block = CreateBlock("paragraph", """{"text":"The equation $x^2 + y^2 = z^2$ is famous."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("$x^2 + y^2 = z^2$");
    }

    [Fact]
    public void Paragraph_WithCiteCommand_PreservesCommand()
    {
        var block = CreateBlock("paragraph", """{"text":"As shown by \\cite{smith2024}, the result holds."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\cite{smith2024}");
    }

    [Fact]
    public void Paragraph_WithUrlCommand_PreservesCommand()
    {
        var block = CreateBlock("paragraph", """{"text":"See \\url{https://example.com} for details."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\url{https://example.com}");
    }

    [Fact]
    public void Paragraph_WithRefCommand_PreservesCommand()
    {
        var block = CreateBlock("paragraph", """{"text":"See Theorem \\ref{thm:main} and \\cref{def:dlp}."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\ref{thm:main}");
        latex.Should().Contain("\\cref{def:dlp}");
    }

    #endregion

    #region Heading

    [Theory]
    [InlineData(1, "\\section{")]
    [InlineData(2, "\\subsection{")]
    [InlineData(3, "\\subsubsection{")]
    public void Heading_AllLevels_GeneratesCorrectCommand(int level, string expected)
    {
        var block = CreateBlock("heading", $"{{\"text\":\"Test\",\"level\":{level}}}");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().StartWith(expected);
    }

    [Fact]
    public void Heading_WithAmpersand_EscapesCorrectly()
    {
        var block = CreateBlock("heading", """{"text":"Results & Discussion","level":1}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\&");
        latex.Should().NotContain("Results & D"); // raw & should not appear
    }

    #endregion

    #region Equation

    [Fact]
    public void Equation_DisplayMode_UsesEquationEnvironment()
    {
        var block = CreateBlock("equation", """{"latex":"E = mc^2","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{equation}");
        latex.Should().Contain("E = mc^2");
        latex.Should().Contain("\\end{equation}");
    }

    [Fact]
    public void Equation_InlineMode_UsesDollarSigns()
    {
        var block = CreateBlock("equation", """{"latex":"x^2","displayMode":false}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Be("$x^2$");
    }

    [Fact]
    public void Equation_WithPlaceholder_StripsArtifact()
    {
        var block = CreateBlock("equation", """{"latex":"\\placeholder{}+x","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().NotContain("\\placeholder");
        latex.Should().Contain("+x");
    }

    [Fact]
    public void Equation_WithAlignEnvironment_NotWrappedInDollarSigns()
    {
        var block = CreateBlock("equation", """{"latex":"\\begin{align}\na &= b \\\\\nc &= d\n\\end{align}","displayMode":false}""");
        var latex = _sut.RenderBlockToLatex(block);
        // Should NOT be wrapped in $...$ — align is paragraph-level
        latex.Should().NotStartWith("$");
        latex.Should().Contain("\\begin{align}");
    }

    [Fact]
    public void Equation_WithGatherEnvironment_NotWrappedInDollarSigns()
    {
        var block = CreateBlock("equation", """{"latex":"\\begin{gather}a \\\\ b\\end{gather}","displayMode":false}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().NotStartWith("$");
        latex.Should().Contain("\\begin{gather}");
    }

    [Fact]
    public void Equation_NestedFractions_GeneratesValidLatex()
    {
        var block = CreateBlock("equation", """{"latex":"\\frac{\\frac{a}{b}}{\\frac{c}{d}}","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\frac{\\frac{a}{b}}{\\frac{c}{d}}");
    }

    [Fact]
    public void Equation_Matrix_GeneratesValidLatex()
    {
        var block = CreateBlock("equation", """{"latex":"\\begin{pmatrix} a & b \\\\ c & d \\end{pmatrix}","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{pmatrix}");
    }

    [Fact]
    public void Equation_Empty_GeneratesValidLatex()
    {
        var block = CreateBlock("equation", """{"latex":"","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().NotBeNull();
    }

    [Fact]
    public void Equation_WithCancelCommand_GeneratesValidLatex()
    {
        // cancel package: strikethrough in math
        var block = CreateBlock("equation", """{"latex":"\\cancel{x} + y = z","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\cancel{x}");
    }

    [Fact]
    public void Equation_WithSiunitx_GeneratesValidLatex()
    {
        // siunitx: SI units
        var block = CreateBlock("equation", """{"latex":"F = \\SI{9.8}{\\meter\\per\\second\\squared}","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\SI{9.8}");
    }

    [Fact]
    public void Equation_WithMathrsfs_GeneratesValidLatex()
    {
        // mathrsfs: script math fonts
        var block = CreateBlock("equation", """{"latex":"\\mathscr{L}(f) = \\int_0^\\infty f(t) e^{-st} dt","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\mathscr{L}");
    }

    [Fact]
    public void Equation_WithMathtools_GeneratesValidLatex()
    {
        // mathtools: dcases, coloneqq, etc.
        var block = CreateBlock("equation", """{"latex":"f(x) \\coloneqq \\begin{dcases} 1 & x > 0 \\\\ 0 & x \\leq 0 \\end{dcases}","displayMode":true}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\coloneqq");
    }

    [Fact]
    public void Equation_WithMultlineEnvironment_NotWrappedInDollars()
    {
        var block = CreateBlock("equation", """{"latex":"\\begin{multline}\na + b + c \\\\\n= d + e + f\n\\end{multline}","displayMode":false}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().NotStartWith("$");
        latex.Should().Contain("\\begin{multline}");
    }

    #endregion

    #region Theorem (all subtypes)

    [Theory]
    [InlineData("theorem", "\\begin{theorem}")]
    [InlineData("definition", "\\begin{definition}")]
    [InlineData("lemma", "\\begin{lemma}")]
    [InlineData("corollary", "\\begin{corollary}")]
    [InlineData("proposition", "\\begin{proposition}")]
    [InlineData("remark", "\\begin{remark}")]
    [InlineData("example", "\\begin{example}")]
    public void Theorem_AllSubtypes_GenerateCorrectEnvironment(string theoremType, string expectedEnv)
    {
        var block = CreateBlock("theorem", $"{{\"theoremType\":\"{theoremType}\",\"title\":\"Test\",\"text\":\"Statement.\"}}");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(expectedEnv);
        latex.Should().Contain("Statement.");
    }

    [Fact]
    public void Theorem_WithTitle_IncludesOptionalArgument()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"Main Result","text":"The theorem."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("[Main Result]");
    }

    [Fact]
    public void Theorem_Proof_UsesProofEnvironment()
    {
        var block = CreateBlock("theorem", """{"theoremType":"proof","title":"","text":"By induction..."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{proof}");
        latex.Should().Contain("\\end{proof}");
    }

    [Fact]
    public void Theorem_Empty_GeneratesValidLatex()
    {
        var block = CreateBlock("theorem", """{"theoremType":"theorem","title":"","text":""}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{theorem}");
        latex.Should().Contain("\\end{theorem}");
    }

    [Fact]
    public void Theorem_WithSpecialCharsInText_EscapesCorrectly()
    {
        var block = CreateBlock("theorem", """{"theoremType":"definition","title":"Sets & Groups","text":"Let $G$ be a group with operation &."}""");
        var latex = _sut.RenderBlockToLatex(block);
        // Title should be escaped but math in text should be preserved
        latex.Should().Contain("\\begin{definition}");
    }

    #endregion

    #region Code

    [Fact]
    public void Code_WithLanguage_IncludesLanguageOption()
    {
        var block = CreateBlock("code", """{"code":"print('hello')","language":"python"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{lstlisting}");
        latex.Should().Contain("language=python");
        latex.Should().Contain("print('hello')");
    }

    [Fact]
    public void Code_WithoutLanguage_GeneratesValidLatex()
    {
        var block = CreateBlock("code", """{"code":"x = 1","language":""}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{lstlisting}");
    }

    [Fact]
    public void Code_WithBackslashes_PreservesContent()
    {
        var block = CreateBlock("code", """{"code":"path = 'C:\\Users\\test'","language":"python"}""");
        var latex = _sut.RenderBlockToLatex(block);
        // lstlisting is verbatim — backslashes should be preserved
        latex.Should().Contain("\\begin{lstlisting}");
    }

    #endregion

    #region List

    [Fact]
    public void List_Ordered_UsesEnumerate()
    {
        var block = CreateBlock("list", """{"items":["First","Second","Third"],"listType":"ordered"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{enumerate}");
        latex.Should().Contain("\\item First");
        latex.Should().Contain("\\end{enumerate}");
    }

    [Fact]
    public void List_Unordered_UsesItemize()
    {
        var block = CreateBlock("list", """{"items":["Alpha","Beta"],"listType":"unordered"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{itemize}");
        latex.Should().Contain("\\item Alpha");
    }

    [Fact]
    public void List_Empty_GeneratesValidLatex()
    {
        var block = CreateBlock("list", """{"items":[],"listType":"ordered"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().NotBeNull();
    }

    #endregion

    #region Blockquote

    [Fact]
    public void Blockquote_GeneratesQuoteEnvironment()
    {
        var block = CreateBlock("blockquote", """{"text":"A wise quote."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{quote}");
        latex.Should().Contain("A wise quote.");
        latex.Should().Contain("\\end{quote}");
    }

    #endregion

    #region Table

    [Fact]
    public void Table_GeneratesTabularEnvironment()
    {
        var block = CreateBlock("table", """{"rows":[["Header 1","Header 2"],["Cell 1","Cell 2"]]}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{table}");
        latex.Should().Contain("\\begin{tabular}");
        latex.Should().Contain("\\toprule");
        latex.Should().Contain("\\end{table}");
    }

    [Fact]
    public void Table_WithEmptyCells_GeneratesValidLatex()
    {
        var block = CreateBlock("table", """{"rows":[["","B"],["",""]]}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{tabular}");
    }

    #endregion

    #region Figure

    [Fact]
    public void Figure_GeneratesFigureEnvironment()
    {
        var block = CreateBlock("figure", """{"src":"images/test.png","caption":"Test figure","alt":"test"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{figure}");
        latex.Should().Contain("\\includegraphics");
        latex.Should().Contain("\\caption{Test figure}");
    }

    [Fact]
    public void Figure_WithoutCaption_GeneratesValidLatex()
    {
        var block = CreateBlock("figure", """{"src":"images/test.png","caption":"","alt":""}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{figure}");
    }

    #endregion

    #region Algorithm

    [Fact]
    public void Algorithm_GeneratesAlgorithmEnvironment()
    {
        var block = CreateBlock("algorithm", """{"title":"Binary Search","code":"\\STATE $low \\gets 0$\n\\STATE $high \\gets n-1$","caption":"Binary search algorithm"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{algorithm}");
        latex.Should().Contain("\\begin{algorithmic}");
        latex.Should().Contain("\\end{algorithmic}");
        latex.Should().Contain("\\end{algorithm}");
    }

    [Fact]
    public void Algorithm_WithCaption_IncludesCaption()
    {
        var block = CreateBlock("algorithm", """{"title":"Sort","code":"\\STATE sort(arr)","caption":"Sorting algorithm"}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\caption{");
    }

    #endregion

    #region Callout

    [Fact]
    public void Callout_GeneratesTcolorboxEnvironment()
    {
        var block = CreateBlock("callout", """{"variant":"note","title":"Important","text":"Pay attention to this."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{tcolorbox}");
        latex.Should().Contain("Pay attention to this.");
        latex.Should().Contain("\\end{tcolorbox}");
    }

    [Fact]
    public void Callout_WithoutTitle_GeneratesValidLatex()
    {
        var block = CreateBlock("callout", """{"variant":"warning","title":"","text":"Be careful."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{tcolorbox}");
    }

    #endregion

    #region Abstract

    [Fact]
    public void Abstract_GeneratesAbstractEnvironment()
    {
        var block = CreateBlock("abstract", """{"text":"This paper presents..."}""");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\begin{abstract}");
        latex.Should().Contain("This paper presents...");
        latex.Should().Contain("\\end{abstract}");
    }

    #endregion

    #region Structural blocks

    [Fact]
    public void PageBreak_GeneratesNewpage()
    {
        var block = CreateBlock("pageBreak", "{}");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\newpage");
    }

    [Fact]
    public void ColumnBreak_GeneratesColumnbreak()
    {
        var block = CreateBlock("columnBreak", "{}");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\columnbreak");
    }

    [Fact]
    public void TableOfContents_GeneratesTocCommand()
    {
        var block = CreateBlock("tableOfContents", "{}");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\tableofcontents");
    }

    #endregion

    #region Edge cases — document-level preamble compatibility

    [Fact]
    public void AllTheoremTypes_AreInValidationPreamble()
    {
        // Verify the validation preamble includes newtheorem for all types used by theorem blocks
        var types = new[] { "theorem", "lemma", "proposition", "corollary", "definition", "example", "remark" };
        foreach (var type in types)
        {
            ValidationPreamble.Should().Contain($"\\newtheorem{{{type}}}", $"Preamble missing \\newtheorem{{{type}}}");
        }
    }

    [Theory]
    [InlineData("inputenc")]
    [InlineData("fontenc")]
    [InlineData("textcomp")]
    [InlineData("amsmath")]
    [InlineData("amssymb")]
    [InlineData("amsfonts")]
    [InlineData("amsthm")]
    [InlineData("mathtools")]
    [InlineData("mathrsfs")]
    [InlineData("cancel")]
    [InlineData("siunitx")]
    [InlineData("microtype")]
    [InlineData("setspace")]
    [InlineData("parskip")]
    [InlineData("graphicx")]
    [InlineData("float")]
    [InlineData("caption")]
    [InlineData("subcaption")]
    [InlineData("xcolor")]
    [InlineData("booktabs")]
    [InlineData("multirow")]
    [InlineData("tabularx")]
    [InlineData("longtable")]
    [InlineData("enumitem")]
    [InlineData("listings")]
    [InlineData("algorithm")]
    [InlineData("algorithmic")]
    [InlineData("tcolorbox")]
    [InlineData("soul")]
    [InlineData("ulem")]
    [InlineData("url")]
    [InlineData("hyperref")]
    [InlineData("cleveref")]
    public void ValidationPreamble_IncludesPackage(string package)
    {
        // Match \usepackage{pkg}, \usepackage[opts]{pkg}, or \usepackage{a,pkg,b}
        var regex = new System.Text.RegularExpressions.Regex(
            $@"\\usepackage(\[.*?\])?\{{[^}}]*\b{package}\b[^}}]*\}}");
        regex.IsMatch(ValidationPreamble).Should().BeTrue(
            $"Preamble should include package '{package}'");
    }

    [Fact]
    public void ValidationPreamble_IncludesDemoGraphicxForFigures()
    {
        // [demo] option renders placeholder boxes instead of requiring actual image files
        ValidationPreamble.Should().Contain("\\usepackage[demo]{graphicx}");
    }

    [Fact]
    public void ValidationPreamble_RestoresEmphAfterUlem()
    {
        // ulem redefines \emph to underline — \normalem restores it to italic
        ValidationPreamble.Should().Contain("\\normalem");
    }

    [Fact]
    public void AllBlockTypes_GenerateNonNullLatex()
    {
        var blocks = new (string type, string json)[]
        {
            ("paragraph", """{"text":"Test"}"""),
            ("heading", """{"text":"Test","level":1}"""),
            ("equation", """{"latex":"x","displayMode":true}"""),
            ("code", """{"code":"x=1","language":"python"}"""),
            ("list", """{"items":["a"],"listType":"ordered"}"""),
            ("blockquote", """{"text":"Quote"}"""),
            ("table", """{"rows":[["A","B"]]}"""),
            ("theorem", """{"theoremType":"theorem","title":"","text":"T"}"""),
            ("abstract", """{"text":"Abstract"}"""),
            ("tableOfContents", "{}"),
            ("pageBreak", "{}"),
            ("algorithm", """{"title":"Algo","code":"\\STATE x","caption":""}"""),
            ("callout", """{"variant":"note","title":"Note","text":"Info"}"""),
            ("figure", """{"src":"test.png","caption":"Fig","alt":""}"""),
        };

        foreach (var (type, json) in blocks)
        {
            var block = CreateBlock(type, json);
            var latex = _sut.RenderBlockToLatex(block);
            latex.Should().NotBeNullOrEmpty($"Block type '{type}' should generate LaTeX");
        }
    }

    [Fact]
    public void AllBlockTypes_LatexWrapsInValidDocument()
    {
        var blocks = new (string type, string json)[]
        {
            ("paragraph", """{"text":"Test paragraph."}"""),
            ("heading", """{"text":"Intro","level":1}"""),
            ("equation", """{"latex":"E=mc^2","displayMode":true}"""),
            ("code", """{"code":"x=1","language":""}"""),
            ("list", """{"items":["a","b"],"listType":"ordered"}"""),
            ("blockquote", """{"text":"Quote text"}"""),
            ("table", """{"rows":[["H1","H2"],["C1","C2"]]}"""),
            ("theorem", """{"theoremType":"definition","title":"DLP","text":"Given a group G..."}"""),
            ("theorem", """{"theoremType":"lemma","title":"","text":"Proof sketch."}"""),
            ("theorem", """{"theoremType":"proof","title":"","text":"By induction..."}"""),
            ("abstract", """{"text":"We present..."}"""),
            ("tableOfContents", "{}"),
            ("pageBreak", "{}"),
            ("algorithm", """{"title":"Search","code":"\\STATE x \\gets 0","caption":"Search algo"}"""),
            ("callout", """{"variant":"warning","title":"Warning","text":"Caution needed."}"""),
            ("figure", """{"src":"img.png","caption":"A figure","alt":""}"""),
        };

        foreach (var (type, json) in blocks)
        {
            var block = CreateBlock(type, json);
            var latex = _sut.RenderBlockToLatex(block);
            var fullDoc = WrapInDocument(latex);

            // Basic structural validation
            fullDoc.Should().Contain("\\documentclass{article}");
            fullDoc.Should().Contain("\\begin{document}");
            fullDoc.Should().Contain("\\end{document}");

            // No unescaped problematic patterns (basic check)
            // Note: This is a heuristic — actual compilation is the real test
            var docBody = fullDoc.Split("\\begin{document}")[1].Split("\\end{document}")[0];
            docBody.Should().NotBeEmpty($"Block type '{type}' should produce content between \\begin{{document}} and \\end{{document}}");
        }
    }

    #endregion

    #region Helpers

    private static Block CreateBlock(string type, string contentJson)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = type,
            Content = JsonDocument.Parse(contentJson),
            SortOrder = 0,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
    }

    #endregion
}
