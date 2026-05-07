using FluentAssertions;
using Lilia.Import.Services;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// LILIA-121 (C1) — verifies that <see cref="LatexParser"/> wires
/// <see cref="ClassOptionsParser"/> into the preamble walk so an imported
/// <c>\documentclass[opts]{class}</c> populates the structured metadata
/// fields and strips the recognised tokens from the options blob.
///
/// These are the round-trip tests called out by C1's spec — they pin the
/// parser-level behaviour, paired with the unit tests in
/// <see cref="ClassOptionsParserTests"/> that pin the helper itself.
/// </summary>
public class LatexParserClassOptionsTests
{
    private readonly LatexParser _parser = new();

    [Fact]
    public async Task Import_book_with_twocolumn_11pt_a4paper_syncs_structured_columns()
    {
        var latex = """
            \documentclass[twocolumn,11pt,a4paper]{book}
            \begin{document}
            Hello.
            \end{document}
            """;
        var doc = await _parser.ParseTextAsync(latex);

        doc.Metadata.DocumentClass.Should().Be("book");
        doc.Metadata.Columns.Should().Be(2);
        doc.Metadata.FontSize.Should().Be(11);
        doc.Metadata.PaperSize.Should().Be("a4");
        // Every token landed in a structured field — nothing left to round-
        // trip via LatexDocumentClassOptions, so the leftover blob is empty.
        doc.Metadata.DocumentClassOptions.Should().BeEmpty();
    }

    [Fact]
    public async Task Import_book_with_landscape_keeps_unrecognised_token_in_options()
    {
        var latex = """
            \documentclass[twocolumn,11pt,a4paper,landscape]{book}
            \begin{document}
            Hello.
            \end{document}
            """;
        var doc = await _parser.ParseTextAsync(latex);

        doc.Metadata.DocumentClass.Should().Be("book");
        doc.Metadata.Columns.Should().Be(2);
        doc.Metadata.FontSize.Should().Be(11);
        doc.Metadata.PaperSize.Should().Be("a4");
        // landscape has no structured-column home today, so it must stay in
        // the leftover blob — otherwise the export builder loses it on the
        // next round-trip.
        doc.Metadata.DocumentClassOptions.Should().Be("landscape");
    }

    [Fact]
    public async Task Import_acmart_with_class_specific_options_preserves_them_unchanged()
    {
        // acmart's profile tokens (manuscript / acmsmall / anonymous / …)
        // have no structured-column mapping. The parser must leave them in
        // place verbatim.
        var latex = """
            \documentclass[manuscript,acmsmall]{acmart}
            \begin{document}
            Hello.
            \end{document}
            """;
        var doc = await _parser.ParseTextAsync(latex);

        doc.Metadata.DocumentClass.Should().Be("acmart");
        doc.Metadata.Columns.Should().BeNull();
        doc.Metadata.FontSize.Should().BeNull();
        doc.Metadata.PaperSize.Should().BeNull();
        doc.Metadata.DocumentClassOptions.Should().Be("manuscript,acmsmall");
    }

    [Fact]
    public async Task Import_class_with_no_options_leaves_metadata_at_defaults()
    {
        var latex = """
            \documentclass{article}
            \begin{document}
            Hello.
            \end{document}
            """;
        var doc = await _parser.ParseTextAsync(latex);

        doc.Metadata.DocumentClass.Should().Be("article");
        doc.Metadata.Columns.Should().BeNull();
        doc.Metadata.FontSize.Should().BeNull();
        doc.Metadata.PaperSize.Should().BeNull();
        doc.Metadata.DocumentClassOptions.Should().BeNull();
    }
}
