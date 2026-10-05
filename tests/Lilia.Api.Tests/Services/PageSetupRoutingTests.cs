using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Xunit;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// Which documents go to LaTeX because Typst would ignore their page setup (5 Oct review): the
/// preview and the default PDF export showed a Typst PDF without the margins, columns, headers,
/// page numbering and the rest that the settings and Ask Lilia said were applied.
/// </summary>
public class PageSetupRoutingTests
{
    private static Document Plain() => new() { Id = Guid.NewGuid(), OwnerId = "u", Title = "T" };

    [Fact]
    public void A_document_on_the_default_page_setup_keeps_Typst() =>
        PageSetupRouting.WhyLatex(Plain()).Should().BeNull();

    [Fact]
    public void Paper_font_family_and_size_alone_keep_Typst_because_Typst_honours_them()
    {
        var d = Plain(); d.PaperSize = "letter"; d.FontFamily = "palatino"; d.FontSize = 10;
        PageSetupRouting.WhyLatex(d).Should().BeNull();
    }

    [Theory]
    [InlineData("margins")]
    [InlineData("landscape")]
    [InlineData("columns")]
    [InlineData("line spacing")]
    [InlineData("paragraph indent")]
    [InlineData("page numbering")]
    [InlineData("header or footer")]
    [InlineData("custom preamble")]
    [InlineData("document class")]
    [InlineData("class options")]
    public void Each_setting_Typst_ignores_sends_the_document_to_LaTeX(string what)
    {
        var d = Plain();
        switch (what)
        {
            case "margins": d.MarginLeft = "3cm"; break;
            case "landscape": d.Orientation = "landscape"; break;
            case "columns": d.Columns = 2; break;
            case "line spacing": d.LineSpacing = 1.5; break;
            case "paragraph indent": d.ParagraphIndent = "none"; break;
            case "page numbering": d.PageNumbering = "roman"; break;
            case "header or footer": d.HeaderLeft = "Lecture 8"; break;
            case "custom preamble": d.CustomPreamble = "\\newcommand{\\R}{\\mathbb{R}}"; break;
            case "document class": d.LatexDocumentClass = "report"; break;
            case "class options": d.LatexDocumentClassOptions = "twoside"; break;
        }
        PageSetupRouting.WhyLatex(d).Should().Contain(what);
    }

    [Fact]
    public void Arabic_numbering_and_article_are_the_defaults_not_page_setup()
    {
        var d = Plain(); d.PageNumbering = "arabic"; d.LatexDocumentClass = "article"; d.Columns = 1;
        PageSetupRouting.WhyLatex(d).Should().BeNull();
    }

    [Fact]
    public void Typst_uses_the_documents_font_size_within_reason()
    {
        TypstExportService.TypstFontSize(12).Should().Be(12);
        TypstExportService.TypstFontSize(10).Should().Be(10);
        TypstExportService.TypstFontSize(0).Should().Be(12);
        TypstExportService.TypstFontSize(400).Should().Be(12);
    }

    [Fact]
    public void Export_options_no_longer_carry_a_size_and_paper_that_override_the_document()
    {
        var o = new LaTeXExportOptions();
        o.FontSize.Should().BeNull();
        o.PaperSize.Should().BeNull();
    }
}
