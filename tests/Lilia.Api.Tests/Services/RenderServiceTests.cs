using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Microsoft.Extensions.Logging;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for RenderService - testing the pure rendering logic without database dependencies.
/// Database-dependent tests (GetPageCountAsync, GetSectionsAsync, RenderPageAsync, etc.)
/// should be covered by integration tests.
/// </summary>
public class RenderServiceTests
{
    private readonly Mock<ILogger<RenderService>> _loggerMock;

    public RenderServiceTests()
    {
        _loggerMock = new Mock<ILogger<RenderService>>();
    }

    #region RenderBlockToHtml Tests

    [Fact]
    public void RenderBlockToHtml_HeadingBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Test Heading", 1);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Be("<h1 class=\"heading\">Test Heading</h1>");
    }

    [Theory]
    [InlineData(1, "h1")]
    [InlineData(2, "h2")]
    [InlineData(3, "h3")]
    [InlineData(4, "h4")]
    [InlineData(5, "h5")]
    [InlineData(6, "h6")]
    public void RenderBlockToHtml_HeadingWithDifferentLevels_ReturnsCorrectTag(int level, string expectedTag)
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Test", level);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().StartWith($"<{expectedTag}");
        result.Should().EndWith($"</{expectedTag}>");
    }

    [Fact]
    public void RenderBlockToHtml_HeadingWithHtmlCharacters_EscapesCorrectly()
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Test <script>alert(xss)</script>", 1);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().NotContain("<script>");
        result.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public void RenderBlockToHtml_ParagraphBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateParagraphBlock(Guid.NewGuid(), 0, "This is a test paragraph.");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Be("<p class=\"paragraph\">This is a test paragraph.</p>");
    }

    [Fact]
    public void RenderBlockToHtml_ParagraphWithBold_ProcessesBoldCorrectly()
    {
        // Arrange
        var block = CreateParagraphBlock(Guid.NewGuid(), 0, "This is **bold** text.");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<strong>bold</strong>");
    }

    [Fact]
    public void RenderBlockToHtml_ParagraphWithItalic_ProcessesItalicCorrectly()
    {
        // Arrange
        var block = CreateParagraphBlock(Guid.NewGuid(), 0, "This is *italic* text.");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<em>italic</em>");
    }

    [Fact]
    public void RenderBlockToHtml_EquationBlockDisplayMode_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateEquationBlock(Guid.NewGuid(), 0, "E = mc^2", true);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("class=\"equation display-math\"");
        result.Should().Contain("data-latex=\"E = mc^2\"");
    }

    [Fact]
    public void RenderBlockToHtml_EquationBlockInlineMode_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateEquationBlock(Guid.NewGuid(), 0, "x^2", false);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("class=\"inline-math\"");
    }

    [Fact]
    public void RenderBlockToHtml_CodeBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateCodeBlock(Guid.NewGuid(), 0, "console.log(hello)", "javascript");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("class=\"code-block\"");
        result.Should().Contain("data-language=\"javascript\"");
        result.Should().Contain("console.log(hello)");
    }

    [Fact]
    public void RenderBlockToHtml_ListBlockOrdered_ReturnsOrderedList()
    {
        // Arrange
        var block = CreateListBlock(Guid.NewGuid(), 0, new[] { "Item 1", "Item 2" }, "ordered");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().StartWith("<ol");
        result.Should().EndWith("</ol>");
        result.Should().Contain("<li>Item 1</li>");
        result.Should().Contain("<li>Item 2</li>");
    }

    [Fact]
    public void RenderBlockToHtml_ListBlockUnordered_ReturnsUnorderedList()
    {
        // Arrange
        var block = CreateListBlock(Guid.NewGuid(), 0, new[] { "Item 1" }, "unordered");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().StartWith("<ul");
        result.Should().EndWith("</ul>");
    }

    [Fact]
    public void RenderBlockToHtml_BlockquoteBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateBlockquoteBlock(Guid.NewGuid(), 0, "Famous quote here");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<blockquote class=\"blockquote\">");
        result.Should().Contain("Famous quote here");
    }

    [Fact]
    public void RenderBlockToHtml_TheoremBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateTheoremBlock(Guid.NewGuid(), 0, "theorem", "Pythagorean", "a^2 + b^2 = c^2");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("class=\"theorem theorem-theorem\"");
        result.Should().Contain("Theorem");
        result.Should().Contain("(Pythagorean)");
        result.Should().Contain("a^2 + b^2 = c^2");
    }

    [Fact]
    public void RenderBlockToHtml_UnknownBlockType_ReturnsEmptyDiv()
    {
        // Arrange
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "unknown_type",
            Content = JsonDocument.Parse("{}"),
            SortOrder = 0
        };
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("block-unknown_type");
    }

    [Fact]
    public void RenderBlockToHtml_FigureBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateFigureBlock(Guid.NewGuid(), 0, "/images/test.png", "Test image", "Figure 1: Test");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<figure class=\"figure\">");
        result.Should().Contain("src=\"/images/test.png\"");
        result.Should().Contain("alt=\"Test image\"");
        result.Should().Contain("<figcaption>Figure 1: Test</figcaption>");
    }

    [Fact]
    public void RenderBlockToHtml_TableBlock_ReturnsCorrectHtml()
    {
        // Arrange
        var block = CreateTableBlock(Guid.NewGuid(), 0);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<table class=\"table\">");
        result.Should().Contain("<th>");
        result.Should().Contain("<td>");
    }

    #endregion

    #region RenderBlockToLatex Tests

    [Fact]
    public void RenderBlockToLatex_HeadingLevel1_ReturnsSection()
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Introduction", 1);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Be("\\section{Introduction}");
    }

    [Fact]
    public void RenderBlockToLatex_HeadingLevel2_ReturnsSubsection()
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Methods", 2);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Be("\\subsection{Methods}");
    }

    [Fact]
    public void RenderBlockToLatex_HeadingLevel3_ReturnsSubsubsection()
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Details", 3);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Be("\\subsubsection{Details}");
    }

    [Fact]
    public void RenderBlockToLatex_HeadingWithSpecialChars_EscapesCorrectly()
    {
        // Arrange
        var block = CreateHeadingBlock(Guid.NewGuid(), 0, "Test & Results", 1);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\&");
    }

    [Fact]
    public void RenderBlockToLatex_Equation_ReturnsEquationEnvironment()
    {
        // Arrange
        var block = CreateEquationBlock(Guid.NewGuid(), 0, "E = mc^2", true);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{equation}");
        result.Should().Contain("E = mc^2");
        result.Should().Contain("\\end{equation}");
    }

    [Fact]
    public void RenderBlockToLatex_InlineEquation_ReturnsDollarSigns()
    {
        // Arrange
        var block = CreateEquationBlock(Guid.NewGuid(), 0, "x^2", false);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Be("$x^2$");
    }

    [Fact]
    public void RenderBlockToLatex_CodeBlock_ReturnsLstlisting()
    {
        // Arrange
        var block = CreateCodeBlock(Guid.NewGuid(), 0, "print(hello)", "python");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{lstlisting}[language=python]");
        result.Should().Contain("print(hello)");
        result.Should().Contain("\\end{lstlisting}");
    }

    [Fact]
    public void RenderBlockToLatex_OrderedList_ReturnsEnumerate()
    {
        // Arrange
        var block = CreateListBlock(Guid.NewGuid(), 0, new[] { "First", "Second" }, "ordered");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{enumerate}");
        result.Should().Contain("\\item First");
        result.Should().Contain("\\item Second");
        result.Should().Contain("\\end{enumerate}");
    }

    [Fact]
    public void RenderBlockToLatex_UnorderedList_ReturnsItemize()
    {
        // Arrange
        var block = CreateListBlock(Guid.NewGuid(), 0, new[] { "Item" }, "unordered");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{itemize}");
        result.Should().Contain("\\end{itemize}");
    }

    [Fact]
    public void RenderBlockToLatex_Theorem_ReturnsTheoremEnvironment()
    {
        // Arrange
        var block = CreateTheoremBlock(Guid.NewGuid(), 0, "theorem", "Main", "The proof is left as an exercise.");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{theorem}[Main]");
        result.Should().Contain("\\end{theorem}");
    }

    [Fact]
    public void RenderBlockToLatex_Blockquote_ReturnsQuoteEnvironment()
    {
        // Arrange
        var block = CreateBlockquoteBlock(Guid.NewGuid(), 0, "Famous words");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{quote}");
        result.Should().Contain("Famous words");
        result.Should().Contain("\\end{quote}");
    }

    [Fact]
    public void RenderBlockToLatex_Figure_ReturnsFigureEnvironment()
    {
        // Arrange
        var block = CreateFigureBlock(Guid.NewGuid(), 0, "image.png", "", "A caption");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{figure}");
        result.Should().Contain("\\includegraphics");
        result.Should().Contain("\\caption{A caption}");
        result.Should().Contain("\\end{figure}");
    }

    [Fact]
    public void RenderBlockToLatex_Table_ReturnsTabularEnvironment()
    {
        // Arrange
        var block = CreateTableBlock(Guid.NewGuid(), 0);
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().Contain("\\begin{table}");
        result.Should().Contain("\\begin{tabular}");
        result.Should().Contain("\\toprule");
        result.Should().Contain("\\midrule");
        result.Should().Contain("\\bottomrule");
        result.Should().Contain("\\end{tabular}");
        result.Should().Contain("\\end{table}");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RenderBlockToHtml_EmptyParagraph_ReturnsEmptyParagraphTag()
    {
        // Arrange
        var block = CreateParagraphBlock(Guid.NewGuid(), 0, "");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Be("<p class=\"paragraph\"></p>");
    }

    [Fact]
    public void RenderBlockToHtml_WithCitation_ProcessesCiteCommand()
    {
        // Arrange
        var block = CreateParagraphBlock(Guid.NewGuid(), 0, "As shown by \\cite{smith2020}");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<cite data-cite=\"smith2020\">");
        result.Should().Contain("[smith2020]");
    }

    [Fact]
    public void RenderBlockToHtml_WithReference_ProcessesRefCommand()
    {
        // Arrange
        var block = CreateParagraphBlock(Guid.NewGuid(), 0, "See \\ref{fig:example}");
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToHtml(block);

        // Assert
        result.Should().Contain("<a class=\"ref\" data-ref=\"fig:example\">");
    }

    [Fact]
    public void RenderBlockToLatex_UnknownType_ReturnsComment()
    {
        // Arrange
        var block = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = Guid.NewGuid(),
            Type = "unknown",
            Content = JsonDocument.Parse("{}"),
            SortOrder = 0
        };
        var sut = CreateRenderServiceWithoutDb();

        // Act
        var result = sut.RenderBlockToLatex(block);

        // Assert
        result.Should().StartWith("% Unknown block type:");
    }

    #endregion

    #region Helper Methods

    private RenderService CreateRenderServiceWithoutDb()
    {
        // Create RenderService with null context - only use for methods that don't need DB
        return new RenderService(null!, _loggerMock.Object);
    }

    private static Block CreateHeadingBlock(Guid documentId, int sortOrder, string text, int level)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "heading",
            Content = JsonDocument.Parse($"{{\"text\": \"{EscapeJson(text)}\", \"level\": {level}}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateParagraphBlock(Guid documentId, int sortOrder, string text)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "paragraph",
            Content = JsonDocument.Parse($"{{\"text\": \"{EscapeJson(text)}\"}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateEquationBlock(Guid documentId, int sortOrder, string latex, bool displayMode)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "equation",
            Content = JsonDocument.Parse($"{{\"latex\": \"{EscapeJson(latex)}\", \"displayMode\": {displayMode.ToString().ToLower()}}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateCodeBlock(Guid documentId, int sortOrder, string code, string language)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "code",
            Content = JsonDocument.Parse($"{{\"code\": \"{EscapeJson(code)}\", \"language\": \"{EscapeJson(language)}\"}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateListBlock(Guid documentId, int sortOrder, string[] items, string listType)
    {
        var itemsJson = string.Join(",", items.Select(i => $"\"{EscapeJson(i)}\""));
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "list",
            Content = JsonDocument.Parse($"{{\"items\": [{itemsJson}], \"listType\": \"{listType}\"}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateBlockquoteBlock(Guid documentId, int sortOrder, string text)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "blockquote",
            Content = JsonDocument.Parse($"{{\"text\": \"{EscapeJson(text)}\"}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateTheoremBlock(Guid documentId, int sortOrder, string theoremType, string title, string text)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "theorem",
            Content = JsonDocument.Parse($"{{\"theoremType\": \"{EscapeJson(theoremType)}\", \"title\": \"{EscapeJson(title)}\", \"text\": \"{EscapeJson(text)}\"}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateFigureBlock(Guid documentId, int sortOrder, string src, string alt, string caption)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "figure",
            Content = JsonDocument.Parse($"{{\"src\": \"{EscapeJson(src)}\", \"alt\": \"{EscapeJson(alt)}\", \"caption\": \"{EscapeJson(caption)}\"}}"),
            SortOrder = sortOrder
        };
    }

    private static Block CreateTableBlock(Guid documentId, int sortOrder)
    {
        return new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = documentId,
            Type = "table",
            Content = JsonDocument.Parse("{\"rows\": [[\"Header 1\", \"Header 2\"], [\"Cell 1\", \"Cell 2\"]]}"),
            SortOrder = sortOrder
        };
    }

    private static string EscapeJson(string text)
    {
        return text
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }

    #endregion
}
