using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Tests inline formatting in both HTML (ProcessInlineContent via RenderBlockToHtml)
/// and LaTeX (ProcessLatexText via RenderBlockToLatex) renderers.
/// Ensures both output paths handle the same set of inline markers consistently.
/// </summary>
public class InlineFormattingTests
{
    private readonly RenderService _sut;

    public InlineFormattingTests()
    {
        var logger = new Mock<ILogger<RenderService>>();
        _sut = new RenderService(null!, logger.Object);
    }

    // ── LaTeX Output ────────────────────────────────────────────────

    #region LaTeX — Bold, Italic, Underline, Strike, Code

    [Fact]
    public void Latex_Bold_ConvertsToTextbf()
    {
        var block = Paragraph("This is **bold** text.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\textbf{bold}");
        latex.Should().NotContain("**");
    }

    [Fact]
    public void Latex_Italic_ConvertsToEmph()
    {
        var block = Paragraph("This is *italic* text.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\emph{italic}");
    }

    [Fact]
    public void Latex_Underline_ConvertsToUnderline()
    {
        var block = Paragraph("This is __underlined__ text.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\underline{underlined}");
    }

    [Fact]
    public void Latex_Strike_ConvertsToSt()
    {
        var block = Paragraph("This is ~~struck~~ text.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\st{struck}");
    }

    [Fact]
    public void Latex_InlineCode_ConvertsToTexttt()
    {
        var block = Paragraph("Use `console.log()` to debug.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\texttt{console.log()}");
    }

    [Fact]
    public void Latex_BoldAndItalic_BothConvert()
    {
        var block = Paragraph("This is **bold** and *italic*.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain(@"\textbf{bold}");
        latex.Should().Contain(@"\emph{italic}");
    }

    #endregion

    #region LaTeX — Math preservation

    [Fact]
    public void Latex_InlineMath_PreservedUnchanged()
    {
        var block = Paragraph("The equation $x^2 + y^2 = z^2$ is Pythagorean.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("$x^2 + y^2 = z^2$");
    }

    [Fact]
    public void Latex_DisplayMath_PreservedUnchanged()
    {
        var block = Paragraph("Consider $$\\int_0^1 x dx$$.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("$$\\int_0^1 x dx$$");
    }

    [Fact]
    public void Latex_MathWithSpecialChars_NotEscaped()
    {
        // Underscores inside $...$ should NOT be escaped
        var block = Paragraph("We define $x_1 + x_2 = y$.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("$x_1 + x_2 = y$");
        latex.Should().NotContain("x\\_1");
    }

    #endregion

    #region LaTeX — Commands preservation

    [Fact]
    public void Latex_Cite_PreservedUnchanged()
    {
        var block = Paragraph("As shown by \\cite{smith2024}, the result holds.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\cite{smith2024}");
    }

    [Fact]
    public void Latex_Ref_PreservedUnchanged()
    {
        var block = Paragraph("See Theorem \\ref{thm:main}.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\ref{thm:main}");
    }

    [Fact]
    public void Latex_Cref_PreservedUnchanged()
    {
        var block = Paragraph("By \\cref{def:dlp}, we have...");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\cref{def:dlp}");
    }

    [Fact]
    public void Latex_Url_PreservedUnchanged()
    {
        var block = Paragraph("Visit \\url{https://example.com}.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\url{https://example.com}");
    }

    [Fact]
    public void Latex_Href_PreservedUnchanged()
    {
        var block = Paragraph("See \\href{https://example.com}{this link}.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("\\href{https://example.com}{this link}");
    }

    #endregion

    #region LaTeX — Special character escaping

    [Fact]
    public void Latex_Ampersand_Escaped()
    {
        var block = Paragraph("R&D department.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("R\\&D");
    }

    [Fact]
    public void Latex_Percent_Escaped()
    {
        var block = Paragraph("100% success rate.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("100\\%");
    }

    [Fact]
    public void Latex_Hash_Escaped()
    {
        var block = Paragraph("Issue #42.");
        var latex = _sut.RenderBlockToLatex(block);
        latex.Should().Contain("Issue \\#42");
    }

    #endregion

    // ── HTML Output ─────────────────────────────────────────────────

    #region HTML — Bold, Italic, Underline, Strike, Code

    [Fact]
    public void Html_Bold_ConvertsToStrong()
    {
        var block = Paragraph("This is **bold** text.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("<strong>bold</strong>");
    }

    [Fact]
    public void Html_Italic_ConvertsToEm()
    {
        var block = Paragraph("This is *italic* text.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("<em>italic</em>");
    }

    [Fact]
    public void Html_Underline_ConvertsToU()
    {
        var block = Paragraph("This is __underlined__ text.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("<u>underlined</u>");
    }

    [Fact]
    public void Html_Strike_ConvertsToDel()
    {
        var block = Paragraph("This is ~~struck~~ text.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("<del>struck</del>");
    }

    [Fact]
    public void Html_InlineCode_ConvertsToCode()
    {
        var block = Paragraph("Use `console.log()` to debug.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("<code>console.log()</code>");
    }

    #endregion

    #region HTML — Links and references

    [Fact]
    public void Html_Cite_ConvertsToTag()
    {
        var block = Paragraph("As shown by \\cite{smith2024}.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("data-cite=\"smith2024\"");
        html.Should().Contain("[smith2024]");
    }

    [Fact]
    public void Html_Ref_ConvertsToLink()
    {
        var block = Paragraph("See \\ref{thm:main}.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("data-ref=\"thm:main\"");
    }

    [Fact]
    public void Html_Url_ConvertsToAnchor()
    {
        var block = Paragraph("Visit \\url{https://example.com}.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("href=\"https://example.com\"");
        html.Should().Contain("class=\"url\"");
    }

    [Fact]
    public void Html_Href_ConvertsToAnchor()
    {
        var block = Paragraph("See \\href{https://example.com}{this link}.");
        var html = _sut.RenderBlockToHtml(block);
        html.Should().Contain("href=\"https://example.com\"");
        html.Should().Contain(">this link</a>");
    }

    #endregion

    #region Parity — Both renderers handle same markers

    [Theory]
    [InlineData("**bold**", "textbf", "strong")]
    [InlineData("*italic*", "emph", "em")]
    [InlineData("__underline__", "underline", "<u>")]
    [InlineData("~~strike~~", "st{", "<del>")]
    [InlineData("`code`", "texttt", "<code>")]
    public void BothRenderers_HandleSameMarker(string marker, string latexExpected, string htmlExpected)
    {
        var block = Paragraph($"Test {marker} here.");
        var latex = _sut.RenderBlockToLatex(block);
        var html = _sut.RenderBlockToHtml(block);

        latex.Should().Contain(latexExpected, $"LaTeX should handle {marker}");
        html.Should().Contain(htmlExpected, $"HTML should handle {marker}");
    }

    #endregion

    // ── Helpers ──────────────────────────────────────────────────────

    private static Block Paragraph(string text) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = "paragraph",
        Content = JsonDocument.Parse($"{{\"text\":\"{EscapeJson(text)}\"}}"),
        SortOrder = 0,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private static string EscapeJson(string text) => text
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\n", "\\n");
}
