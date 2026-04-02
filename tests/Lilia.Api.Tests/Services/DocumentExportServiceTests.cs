using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Import.Models;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Unit tests for DocumentExportService — focused on the inline formatting parser
/// and block-to-ExportBlock mapping logic (static/pure methods).
/// </summary>
public class DocumentExportServiceTests
{
    #region ParseInlineFormatting Tests

    [Fact]
    public void ParseInlineFormatting_PlainText_ReturnsSingleSpan()
    {
        var result = DocumentExportService.ParseInlineFormatting("Hello world");

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("Hello world");
        result[0].Bold.Should().BeFalse();
        result[0].Italic.Should().BeFalse();
    }

    [Fact]
    public void ParseInlineFormatting_NullInput_ReturnsSingleEmptySpan()
    {
        var result = DocumentExportService.ParseInlineFormatting(null);

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("");
    }

    [Fact]
    public void ParseInlineFormatting_EmptyString_ReturnsSingleEmptySpan()
    {
        var result = DocumentExportService.ParseInlineFormatting("");

        result.Should().HaveCount(1);
        result[0].Text.Should().Be("");
    }

    [Fact]
    public void ParseInlineFormatting_BoldText_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("This is **bold** text");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("This is ");
        result[0].Bold.Should().BeFalse();
        result[1].Text.Should().Be("bold");
        result[1].Bold.Should().BeTrue();
        result[2].Text.Should().Be(" text");
        result[2].Bold.Should().BeFalse();
    }

    [Fact]
    public void ParseInlineFormatting_ItalicText_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("This is *italic* text");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("This is ");
        result[1].Text.Should().Be("italic");
        result[1].Italic.Should().BeTrue();
        result[2].Text.Should().Be(" text");
    }

    [Fact]
    public void ParseInlineFormatting_UnderlineText_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("This is __underlined__ text");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("This is ");
        result[1].Text.Should().Be("underlined");
        result[1].Underline.Should().BeTrue();
        result[2].Text.Should().Be(" text");
    }

    [Fact]
    public void ParseInlineFormatting_StrikethroughText_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("This is ~~struck~~ text");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("This is ");
        result[1].Text.Should().Be("struck");
        result[1].Strikethrough.Should().BeTrue();
        result[2].Text.Should().Be(" text");
    }

    [Fact]
    public void ParseInlineFormatting_InlineCode_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("Use `console.log()` here");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("Use ");
        result[1].Text.Should().Be("console.log()");
        result[1].FontFamily.Should().Be("Consolas");
        result[2].Text.Should().Be(" here");
    }

    [Fact]
    public void ParseInlineFormatting_InlineMath_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("The formula $x^2$ is quadratic");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("The formula ");
        result[1].Text.Should().Be("x^2");
        result[1].Equation.Should().Be("x^2");
        result[2].Text.Should().Be(" is quadratic");
    }

    [Fact]
    public void ParseInlineFormatting_MultipleMixed_ParsesAllFormats()
    {
        var result = DocumentExportService.ParseInlineFormatting("**bold** and *italic* and `code`");

        result.Should().HaveCount(5);
        result[0].Text.Should().Be("bold");
        result[0].Bold.Should().BeTrue();
        result[1].Text.Should().Be(" and ");
        result[2].Text.Should().Be("italic");
        result[2].Italic.Should().BeTrue();
        result[3].Text.Should().Be(" and ");
        result[4].Text.Should().Be("code");
        result[4].FontFamily.Should().Be("Consolas");
    }

    [Fact]
    public void ParseInlineFormatting_BoldAndItalicSeparate_ParsesBoth()
    {
        var result = DocumentExportService.ParseInlineFormatting("This is **bold** and *italic*");

        result.Should().HaveCount(4);
        result[0].Text.Should().Be("This is ");
        result[1].Text.Should().Be("bold");
        result[1].Bold.Should().BeTrue();
        result[2].Text.Should().Be(" and ");
        result[3].Text.Should().Be("italic");
        result[3].Italic.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineFormatting_UnmatchedMarkers_TreatedAsPlainText()
    {
        // A single * without matching close should be treated as plain text
        var result = DocumentExportService.ParseInlineFormatting("This has a * lone asterisk");

        // The * won't find a closing match beyond it (the space after * means
        // the search for closing * will find one... let's test carefully)
        // Actually "* lone asterisk" — the * at position 11 finds no closing * after it
        // Wait — there's no closing * in the rest. Let me re-check:
        // "This has a * lone asterisk" — there is no second * so it stays plain
        result.Should().HaveCount(1);
        result[0].Text.Should().Be("This has a * lone asterisk");
    }

    [Fact]
    public void ParseInlineFormatting_ConsecutiveBoldSections_ParsesBoth()
    {
        var result = DocumentExportService.ParseInlineFormatting("**first** **second**");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("first");
        result[0].Bold.Should().BeTrue();
        result[1].Text.Should().Be(" ");
        result[2].Text.Should().Be("second");
        result[2].Bold.Should().BeTrue();
    }

    [Fact]
    public void ParseInlineFormatting_MathWithComplexExpression_ParsesCorrectly()
    {
        var result = DocumentExportService.ParseInlineFormatting("Given $\\int_0^1 f(x) dx$, we find");

        result.Should().HaveCount(3);
        result[0].Text.Should().Be("Given ");
        result[1].Equation.Should().Be("\\int_0^1 f(x) dx");
        result[2].Text.Should().Be(", we find");
    }

    #endregion
}
