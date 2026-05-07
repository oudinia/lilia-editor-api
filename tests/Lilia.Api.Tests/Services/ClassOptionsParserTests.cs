using FluentAssertions;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// LILIA-121 (C1) — round-trip safety for class-option syncing on import.
///
/// The parser splits the raw <c>\documentclass[opts]</c> blob into the
/// structured <c>Document</c> columns (font size, paper size, columns) and
/// the leftover unrecognised tokens. These tests pin the recognised-token
/// list and the leftover-blob behaviour so a re-export of an imported
/// document doesn't double-emit the same option.
///
/// Spec: <c>lilia-docs/strategy/2026-05-06-documentclass-first/jiras/C1-import-sync-structured.md</c>
/// </summary>
public class ClassOptionsParserTests
{
    [Fact]
    public void Parse_recognises_book_with_twocolumn_11pt_a4paper()
    {
        var result = ClassOptionsParser.Parse("twocolumn,11pt,a4paper");

        result.Columns.Should().Be(2);
        result.FontSize.Should().Be(11);
        result.PaperSize.Should().Be("a4");
        // Every token landed in a structured column → no leftovers.
        result.RemainingOptions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_keeps_landscape_in_remaining_when_others_recognised()
    {
        var result = ClassOptionsParser.Parse("twocolumn,11pt,a4paper,landscape");

        result.Columns.Should().Be(2);
        result.FontSize.Should().Be(11);
        result.PaperSize.Should().Be("a4");
        // landscape isn't (yet) a structured column — stays in the blob so
        // the export builder can re-emit it on the next round-trip.
        result.RemainingOptions.Should().Be("landscape");
    }

    [Fact]
    public void Parse_preserves_unrecognised_acmart_tokens_unchanged()
    {
        // acmart-specific profile tokens have no structured-column mapping;
        // the parser must pass them through untouched so the export builder
        // can re-emit "manuscript,acmsmall" verbatim.
        var result = ClassOptionsParser.Parse("manuscript,acmsmall");

        result.Columns.Should().BeNull();
        result.FontSize.Should().BeNull();
        result.PaperSize.Should().BeNull();
        result.RemainingOptions.Should().Be("manuscript,acmsmall");
    }

    [Theory]
    [InlineData("10pt", 10)]
    [InlineData("11pt", 11)]
    [InlineData("12pt", 12)]
    public void Parse_extracts_each_supported_font_size(string token, int expected)
    {
        var result = ClassOptionsParser.Parse(token);

        result.FontSize.Should().Be(expected);
        result.RemainingOptions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a4paper", "a4")]
    [InlineData("letterpaper", "letter")]
    [InlineData("a5paper", "a5")]
    [InlineData("legalpaper", "legal")]
    [InlineData("executivepaper", "executive")]
    [InlineData("b5paper", "b5")]
    public void Parse_extracts_each_supported_paper_size(string token, string expected)
    {
        var result = ClassOptionsParser.Parse(token);

        result.PaperSize.Should().Be(expected);
        result.RemainingOptions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("onecolumn", 1)]
    [InlineData("twocolumn", 2)]
    public void Parse_extracts_columns(string token, int expected)
    {
        var result = ClassOptionsParser.Parse(token);

        result.Columns.Should().Be(expected);
        result.RemainingOptions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("oneside")]
    [InlineData("twoside")]
    [InlineData("titlepage")]
    [InlineData("notitlepage")]
    [InlineData("draft")]
    [InlineData("final")]
    public void Parse_keeps_unrecognised_class_options_in_remaining(string token)
    {
        var result = ClassOptionsParser.Parse(token);

        result.FontSize.Should().BeNull();
        result.PaperSize.Should().BeNull();
        result.Columns.Should().BeNull();
        result.RemainingOptions.Should().Be(token);
    }

    [Fact]
    public void Parse_returns_null_remaining_for_null_input()
    {
        var result = ClassOptionsParser.Parse(null);

        result.FontSize.Should().BeNull();
        result.PaperSize.Should().BeNull();
        result.Columns.Should().BeNull();
        result.RemainingOptions.Should().BeNull();
    }

    [Fact]
    public void Parse_returns_empty_remaining_for_empty_input()
    {
        var result = ClassOptionsParser.Parse(string.Empty);

        result.RemainingOptions.Should().BeEmpty();
    }

    [Fact]
    public void Parse_trims_whitespace_around_tokens()
    {
        var result = ClassOptionsParser.Parse(" 11pt , a4paper , twocolumn , landscape ");

        result.FontSize.Should().Be(11);
        result.PaperSize.Should().Be("a4");
        result.Columns.Should().Be(2);
        result.RemainingOptions.Should().Be("landscape");
    }

    [Fact]
    public void Parse_skips_empty_tokens_from_double_commas()
    {
        var result = ClassOptionsParser.Parse("11pt,,a4paper,,,landscape");

        result.FontSize.Should().Be(11);
        result.PaperSize.Should().Be("a4");
        result.RemainingOptions.Should().Be("landscape");
    }

    [Fact]
    public void Parse_preserves_token_order_in_remaining_blob()
    {
        // Source order matters because some classes are option-order sensitive
        // (e.g. acmart treats "anonymous,manuscript" differently). We must
        // preserve the original sequence in the leftover blob.
        var result = ClassOptionsParser.Parse("anonymous,11pt,manuscript,acmsmall,review");

        result.FontSize.Should().Be(11);
        result.RemainingOptions.Should().Be("anonymous,manuscript,acmsmall,review");
    }

    [Fact]
    public void Parse_is_case_insensitive_for_recognised_tokens()
    {
        // LaTeX class options are case-sensitive in spec, but real-world
        // imports occasionally come in mixed case from copy-paste. Be
        // forgiving on the recognised set; leave the leftover blob untouched.
        var result = ClassOptionsParser.Parse("11PT,A4Paper,TWOCOLUMN");

        result.FontSize.Should().Be(11);
        result.PaperSize.Should().Be("a4");
        result.Columns.Should().Be(2);
        result.RemainingOptions.Should().BeEmpty();
    }
}
