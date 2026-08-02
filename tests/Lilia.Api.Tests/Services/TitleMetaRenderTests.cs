using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Title / author / date meta: LaTeX token preservation + Typst live-preview
/// inclusion. Regression for "author tokens and date tokens not in preview".
/// </summary>
public class TitleMetaRenderTests
{
    [Theory]
    [InlineData(@"Ada Lovelace \and Alan Turing", @"Ada Lovelace \and Alan Turing")]
    [InlineData(@"Ada\thanks{Corresponding author}", @"Ada\thanks{Corresponding author}")]
    [InlineData(@"\today", @"\today")]
    [InlineData(@"March 2026", @"March 2026")]
    [InlineData(@"Smith & Jones", @"Smith \& Jones")]
    public void FormatTitleMetaLatex_preserves_tokens_and_escapes_text(string input, string expected)
    {
        LaTeXExportService.FormatTitleMetaLatex(input).Should().Be(expected);
        RenderService.FormatTitleMetaLatex(input).Should().Be(expected);
    }

    [Fact]
    public void FormatTitleMetaLatex_does_not_backslash_escape_and_token()
    {
        var result = LaTeXExportService.FormatTitleMetaLatex(@"A \and B");
        result.Should().NotContain(@"\textbackslash{}");
        result.Should().Contain(@"\and");
    }

    [Fact]
    public void PlainTitleMetaForTypst_converts_latex_tokens()
    {
        TypstExportService.PlainTitleMetaForTypst(@"Ada Lovelace \and Alan Turing")
            .Should().Be("Ada Lovelace, Alan Turing");
        TypstExportService.PlainTitleMetaForTypst(@"\today")
            .Should().MatchRegex(@"^[A-Z][a-z]+ \d{1,2}, \d{4}$");
        TypstExportService.PlainTitleMetaForTypst(@"Ada\thanks{note}")
            .Should().Be("Ada");
    }

    [Fact]
    public void TypstExport_includes_author_and_date_from_title_block()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Metadata Title",
            PaperSize = "a4",
            FontFamily = "default",
            Columns = 1,
        };
        var titleBlock = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = BlockTypes.Title,
            SortOrder = 0,
            Content = JsonDocument.Parse(
                """{"title":"Paper Title","author":"Ada Lovelace \\and Alan Turing","date":"\\today"}"""),
        };
        var para = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = BlockTypes.Paragraph,
            SortOrder = 1,
            Content = JsonDocument.Parse("""{"text":"Body."}"""),
        };

        var svc = new TypstExportService();
        var source = svc.BuildTypstDocument(doc, new List<Block> { titleBlock, para });

        source.Should().Contain("Paper Title");
        source.Should().Contain("Ada Lovelace, Alan Turing");
        // \today → formatted calendar date
        source.Should().MatchRegex(@"[A-Z][a-z]+ \d{1,2}, \d{4}");
        source.Should().Contain("Body.");
        // Title block itself must not also render as unsupported comment
        source.Should().NotContain("Unsupported block type");
    }

    [Fact]
    public void TypstExport_expands_today_in_body_paragraph()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Sample Report",
            PaperSize = "a4",
            FontFamily = "default",
            Columns = 1,
        };
        var para = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = BlockTypes.Paragraph,
            SortOrder = 0,
            Content = JsonDocument.Parse("""{"text":"Date: \\today"}"""),
        };

        var source = new TypstExportService().BuildTypstDocument(doc, new List<Block> { para });
        source.Should().NotContain(@"\today");
        source.Should().NotContain(@"\textbackslash");
        source.Should().MatchRegex(@"Date: [A-Z][a-z]+ \d{1,2}, \d{4}");
    }

    [Theory]
    [InlineData(@"Date: \today", true)]
    [InlineData(@"Hello world", false)]
    public void ExpandBareLatexMetaTokensForDisplay_handles_today(string input, bool hasDate)
    {
        var outText = RenderService.ExpandBareLatexMetaTokensForDisplay(input);
        outText.Should().NotContain(@"\today");
        if (hasDate)
            outText.Should().MatchRegex(@"Date: [A-Z][a-z]+ \d{1,2}, \d{4}");
        else
            outText.Should().Be(input);
    }

    [Fact]
    public void FormatInlineContent_preserves_today_for_latex()
    {
        var latex = LaTeXExportService.FormatInlineContent(@"Date: \today");
        latex.Should().Contain(@"\today");
        latex.Should().NotContain(@"\textbackslash");
    }

    [Fact]
    public void TypstExport_title_only_without_author_omits_date_line()
    {
        var doc = new Document
        {
            Id = Guid.NewGuid(),
            Title = "Just A Title",
            PaperSize = "a4",
            FontFamily = "default",
            Columns = 1,
        };
        var para = new Block
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            Type = BlockTypes.Paragraph,
            SortOrder = 0,
            Content = JsonDocument.Parse("""{"text":"Hello"}"""),
        };

        var svc = new TypstExportService();
        var source = svc.BuildTypstDocument(doc, new List<Block> { para });

        source.Should().Contain("Just A Title");
        source.Should().Contain("Hello");
        // Document title is always set; author metadata is not when absent.
        source.Should().Contain("#set document(title:");
        source.Should().NotContain("author:");
    }
}
