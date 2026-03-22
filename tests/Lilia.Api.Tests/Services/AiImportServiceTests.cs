using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Models.AiImport;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for AiImportService — testing heuristic/mock mode only (no real AI calls).
/// All tests use a placeholder API key so the service falls back to heuristic classification.
/// </summary>
public class AiImportServiceTests
{
    private readonly AiImportService _service;

    public AiImportServiceTests()
    {
        var chatClientMock = new Mock<IChatClient>();
        var loggerMock = new Mock<ILogger<AiImportService>>();
        var options = Options.Create(new AiOptions
        {
            Anthropic = new AnthropicOptions { ApiKey = "sk-placeholder" },
            DefaultModel = "test-model"
        });

        _service = new AiImportService(chatClientMock.Object, options, loggerMock.Object);
    }

    #region Block Classification — Equation Detection

    [Theory]
    [InlineData(@"\frac{a}{b} = c")]
    [InlineData(@"\int_0^1 f(x) dx")]
    [InlineData(@"\sum_{i=1}^{n} x_i")]
    [InlineData(@"$$ E = mc^2 $$")]
    [InlineData(@"\begin{equation} x = y \end{equation}")]
    [InlineData(@"\begin{align} x &= y \end{align}")]
    [InlineData(@"\partial f / \partial x")]
    [InlineData(@"\nabla \cdot F")]
    public async Task ClassifyBlock_EquationContent_SuggestsEquation(string content)
    {
        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("equation");
        result.SuggestsChange.Should().BeTrue();
        result.Confidence.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task ClassifyBlock_EquationAlreadyCorrect_KeepsType()
    {
        var result = await _service.ClassifyBlockAsync(@"\frac{a}{b}", "equation");

        result.SuggestedType.Should().Be("equation");
        result.SuggestsChange.Should().BeFalse();
        result.Confidence.Should().Be(0.9);
    }

    #endregion

    #region Block Classification — Code Detection

    [Theory]
    [InlineData("function hello() { return 1; }")]
    [InlineData("def main():\n    print('hello')")]
    [InlineData("class MyService { }")]
    [InlineData("import numpy as np")]
    [InlineData("public static void Main(string[] args)")]
    [InlineData("Console.WriteLine(\"hello\")")]
    public async Task ClassifyBlock_CodeContent_SuggestsCode(string content)
    {
        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("code");
        result.SuggestsChange.Should().BeTrue();
    }

    #endregion

    #region Block Classification — Heading Detection

    [Theory]
    [InlineData("INTRODUCTION")]
    [InlineData("METHODS AND RESULTS")]
    [InlineData("1. Introduction")]
    [InlineData("3. Experimental Setup")]
    [InlineData("# Overview")]
    public async Task ClassifyBlock_HeadingContent_SuggestsHeading(string content)
    {
        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("heading");
        result.SuggestsChange.Should().BeTrue();
    }

    #endregion

    #region Block Classification — List Detection

    [Fact]
    public async Task ClassifyBlock_ListContent_SuggestsList()
    {
        var content = "- First item\n- Second item\n- Third item";

        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("list");
        result.SuggestsChange.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyBlock_NumberedList_SuggestsList()
    {
        var content = "1. First\n2. Second\n3. Third";

        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("list");
        result.SuggestsChange.Should().BeTrue();
    }

    #endregion

    #region Block Classification — Table Detection

    [Fact]
    public async Task ClassifyBlock_TableContent_SuggestsTable()
    {
        var content = "Name | Age | City\nAlice | 30 | NYC\nBob | 25 | LA";

        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("table");
        result.SuggestsChange.Should().BeTrue();
    }

    #endregion

    #region Block Classification — Plain Text

    [Fact]
    public async Task ClassifyBlock_PlainText_SuggestsParagraph()
    {
        var content = "This is a normal paragraph of text about the results of our experiment.";

        var result = await _service.ClassifyBlockAsync(content, "paragraph");

        result.SuggestedType.Should().Be("paragraph");
        result.SuggestsChange.Should().BeFalse();
        result.Confidence.Should().Be(0.9);
        result.Reasoning.Should().Contain("correct");
    }

    #endregion

    #region Fix Formatting

    [Fact]
    public async Task FixFormatting_MultipleSpaces_NormalizesWhitespace()
    {
        var content = "This  has   multiple    spaces";

        var result = await _service.FixFormattingAsync(content, "paragraph");

        result.Should().Be("This has multiple spaces");
    }

    [Fact]
    public async Task FixFormatting_TrailingWhitespace_Trims()
    {
        var content = "Hello world   \nSecond line   ";

        var result = await _service.FixFormattingAsync(content, "paragraph");

        // Each line should be trimmed at the end
        var lines = result.Split('\n');
        foreach (var line in lines)
        {
            line.Should().Be(line.TrimEnd());
        }
    }

    [Fact]
    public async Task FixFormatting_CrlfLineEndings_NormalizesToLf()
    {
        var content = "Line one\r\nLine two\r\nLine three";

        var result = await _service.FixFormattingAsync(content, "paragraph");

        result.Should().NotContain("\r\n");
        result.Should().Contain("\n");
    }

    [Fact]
    public async Task FixFormatting_CodeBlock_PreservesMultipleSpaces()
    {
        var content = "if (x)  {\n    return  y;\n}";

        var result = await _service.FixFormattingAsync(content, "code");

        // Code blocks should preserve multiple spaces (indentation)
        result.Should().Contain("    return  y;");
    }

    [Fact]
    public async Task FixFormatting_EmptyContent_ReturnsEmpty()
    {
        var result = await _service.FixFormattingAsync("", "paragraph");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task FixFormatting_EquationBlock_NormalizesDisplayMathDelimiters()
    {
        var content = @"\[E = mc^2\]";

        var result = await _service.FixFormattingAsync(content, "equation");

        result.Should().Contain("$$");
        result.Should().NotContain(@"\[");
        result.Should().NotContain(@"\]");
    }

    #endregion

    #region Suggest Improvements

    [Fact]
    public async Task SuggestImprovements_EmptyContent_ReturnsWarning()
    {
        var result = await _service.SuggestImprovementsAsync("", "paragraph");

        result.Should().HaveCount(1);
        result[0].Category.Should().Be(QualitySuggestion.Categories.Content);
        result[0].Severity.Should().Be(QualitySuggestion.Severities.Warning);
        result[0].Description.Should().Contain("empty");
    }

    [Fact]
    public async Task SuggestImprovements_MultipleSpaces_ReturnsFormattingInfo()
    {
        var result = await _service.SuggestImprovementsAsync("Text  with  double  spaces", "paragraph");

        result.Should().Contain(s => s.Category == QualitySuggestion.Categories.Formatting);
    }

    [Fact]
    public async Task SuggestImprovements_UnbalancedDollarSigns_ReturnsError()
    {
        var result = await _service.SuggestImprovementsAsync("$x^2", "equation");

        result.Should().Contain(s =>
            s.Category == QualitySuggestion.Categories.Math &&
            s.Severity == QualitySuggestion.Severities.Error);
    }

    [Fact]
    public async Task SuggestImprovements_MismatchedEnvironments_ReturnsError()
    {
        var result = await _service.SuggestImprovementsAsync(
            @"\begin{equation} x = y", "equation");

        result.Should().Contain(s =>
            s.Category == QualitySuggestion.Categories.Math &&
            s.Severity == QualitySuggestion.Severities.Error &&
            s.Description.Contains("Mismatched"));
    }

    [Fact]
    public async Task SuggestImprovements_TypeMismatch_ReturnsSuggestion()
    {
        // Content looks like code but typed as paragraph
        var result = await _service.SuggestImprovementsAsync(
            "function hello() { return 1; }", "paragraph");

        result.Should().Contain(s =>
            s.Category == QualitySuggestion.Categories.Structure);
    }

    [Fact]
    public async Task SuggestImprovements_ShortParagraph_ReturnsInfo()
    {
        var result = await _service.SuggestImprovementsAsync("Short.", "paragraph");

        result.Should().Contain(s =>
            s.Category == QualitySuggestion.Categories.Content &&
            s.Severity == QualitySuggestion.Severities.Info &&
            s.Description.Contains("short", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SuggestImprovements_WellFormedContent_ReturnsEmptyOrMinor()
    {
        var result = await _service.SuggestImprovementsAsync(
            "This is a well-formed paragraph that discusses the experimental methodology used in our study.",
            "paragraph");

        // Should have no errors or warnings — possibly empty or just info-level
        result.Should().NotContain(s => s.Severity == QualitySuggestion.Severities.Error);
    }

    #endregion

    #region Batch Classification

    [Fact]
    public async Task ClassifyBatch_MultipleBlocks_ReturnsCorrectCount()
    {
        var blocks = new List<(string content, string currentType)>
        {
            (@"\frac{a}{b}", "paragraph"),
            ("function test() {}", "paragraph"),
            ("This is a normal sentence.", "paragraph"),
        };

        var results = await _service.ClassifyBatchAsync(blocks);

        results.Should().HaveCount(3);
    }

    [Fact]
    public async Task ClassifyBatch_ClassifiesEachBlock()
    {
        var blocks = new List<(string content, string currentType)>
        {
            (@"\frac{a}{b}", "paragraph"),
            ("function test() {}", "paragraph"),
            ("INTRODUCTION", "paragraph"),
        };

        var results = await _service.ClassifyBatchAsync(blocks);

        results[0].SuggestedType.Should().Be("equation");
        results[1].SuggestedType.Should().Be("code");
        results[2].SuggestedType.Should().Be("heading");
    }

    [Fact]
    public async Task ClassifyBatch_EmptyList_ReturnsEmptyList()
    {
        var blocks = new List<(string content, string currentType)>();

        var results = await _service.ClassifyBatchAsync(blocks);

        results.Should().BeEmpty();
    }

    #endregion
}
