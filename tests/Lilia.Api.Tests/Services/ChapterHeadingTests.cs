using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using HeadingCommands = Lilia.Core.Models.HeadingCommands;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Lilia.Api.Tests.Services;

/// <summary>
/// In a class with \chapter (report, book, memoir, scrbook, scrreprt, amsbook) a level-1 heading is
/// a chapter and level 2 a section, in the preview, the export and the import. Writing \section for
/// level 1 there numbered every section "0.1" (user's decision, 6 Oct 2026). Article is unchanged.
/// </summary>
public class ChapterHeadingTests
{
    private static int _order;

    private static Block Heading(string text, int level, Document? doc = null) => new()
    {
        Id = Guid.NewGuid(),
        Type = "heading",
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text, level })),
        Document = doc!,
    };

    private static Block Paragraph(string text) => new()
    {
        Id = Guid.NewGuid(),
        Type = "paragraph",
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(new { text })),
    };

    private static Document Doc(string cls) => new()
    {
        Id = Guid.NewGuid(), Title = "Notes", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = cls,
    };

    private static string Export(Document doc, List<Block> blocks) =>
        new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, blocks, [], new LaTeXExportOptions());

    [Theory]
    [InlineData("article", 1, "section")]
    [InlineData("article", 2, "subsection")]
    [InlineData("article", 5, "subparagraph")]
    [InlineData("article", 6, "subparagraph")]
    [InlineData("report", 1, "chapter")]
    [InlineData("report", 2, "section")]
    [InlineData("book", 3, "subsection")]
    [InlineData("scrbook", 6, "subparagraph")]
    [InlineData("memoir", 1, "chapter")]
    [InlineData(null, 1, "section")]
    public void A_level_prints_as_its_class_command(string? cls, int level, string command) =>
        HeadingCommands.For(level, cls).Should().Be(command);

    [Theory]
    [InlineData("report", "chapter", 1)]
    [InlineData("report", "section", 2)]
    [InlineData("book", "subparagraph", 6)]
    [InlineData("article", "section", 1)]
    [InlineData("article", "chapter", 1)]
    public void A_command_imports_as_its_class_level(string cls, string command, int level) =>
        HeadingCommands.LevelOf(command, cls).Should().Be(level);

    [Theory]
    [InlineData("report")]
    [InlineData("book")]
    public void The_export_prints_level_1_as_chapter_and_level_2_as_section(string cls)
    {
        var tex = Export(Doc(cls), [Heading("Limits", 1), Paragraph("Body."), Heading("Epsilon and delta", 2)]);
        tex.Should().Contain(@"\chapter{Limits}").And.Contain(@"\section{Epsilon and delta}");
        tex.Should().NotContain(@"\section{Limits}");
    }

    [Fact]
    public void The_export_of_an_article_is_unchanged()
    {
        var tex = Export(Doc("article"), [Heading("Limits", 1), Heading("Epsilon and delta", 2)]);
        tex.Should().Contain(@"\section{Limits}").And.Contain(@"\subsection{Epsilon and delta}");
        tex.Should().NotContain(@"\chapter");
    }

    [Fact]
    public void The_preview_prints_level_1_as_chapter_in_a_report()
    {
        var render = new RenderService(null!, new Mock<ILogger<RenderService>>().Object);
        render.RenderBlockToLatex(Heading("Limits", 1, Doc("report"))).Should().Contain(@"\chapter{Limits}");
        render.RenderBlockToLatex(Heading("Epsilon and delta", 2, Doc("report"))).Should().Contain(@"\section{Epsilon and delta}");
        render.RenderBlockToLatex(Heading("Limits", 1, Doc("article"))).Should().Contain(@"\section{Limits}");
    }

    private static async Task<List<(int Level, string Text)>> ImportHeadings(string tex)
    {
        var parsed = await new LatexParser().ParseTextAsync(tex);
        return parsed.Elements.OfType<ImportHeading>().Select(h => (h.Level, h.Text)).ToList();
    }

    [Fact]
    public async Task A_reports_chapter_imports_as_level_1_and_its_sections_as_level_2()
    {
        var headings = await ImportHeadings("""
            \documentclass{report}
            \begin{document}
            \chapter{Limits}
            Body.
            \section{Epsilon and delta}
            More.
            \subparagraph{Deepest}
            Last.
            \end{document}
            """);
        headings.Should().Equal((1, "Limits"), (2, "Epsilon and delta"), (6, "Deepest"));
    }

    [Fact]
    public async Task An_articles_sections_still_import_as_level_1()
    {
        var headings = await ImportHeadings("""
            \documentclass{article}
            \begin{document}
            \section{Limits}
            Body.
            \subsection{Epsilon and delta}
            More.
            \end{document}
            """);
        headings.Should().Equal((1, "Limits"), (2, "Epsilon and delta"));
    }

    [Fact]
    public async Task A_report_round_trips_its_chapters_and_sections()
    {
        var doc = Doc("report");
        var tex = Export(doc, [Heading("Limits", 1), Paragraph("Body."), Heading("Epsilon and delta", 2), Paragraph("More.")]);
        var headings = await ImportHeadings(tex);
        headings.Should().Equal((1, "Limits"), (2, "Epsilon and delta"));
    }

    [Theory]
    [Trait("Category", "LatexCompile")]
    [InlineData("report")]
    [InlineData("book")]
    public async Task A_report_or_book_numbers_sections_under_their_chapter_not_0_1(string cls)
    {
        var tex = Export(Doc(cls), [
            Heading("Limits", 1), Paragraph("Quartz zebra paragraph."),
            Heading("Epsilon and delta", 2), Paragraph("Jovial wombat paragraph."),
            Heading("Derivatives", 1), Paragraph("Fizzing yak paragraph."),
            Heading("Product rule", 2), Paragraph("Muted owl paragraph."),
        ]);

        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(tex, "pdflatex", timeout: 180);
        var text = await PdfText(pdf);

        text.Should().Contain("Chapter 1").And.Contain("Chapter 2");
        text.Should().MatchRegex(@"1\.1\s+Epsilon and delta").And.MatchRegex(@"2\.1\s+Product rule");
        text.Should().NotMatchRegex(@"0\.\d\s");
    }

    private static async Task<string> PdfText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-chapters-{Guid.NewGuid():N}.pdf");
        await File.WriteAllBytesAsync(path, pdf);
        try
        {
            var psi = new ProcessStartInfo("pdftotext") { RedirectStandardOutput = true, UseShellExecute = false };
            psi.ArgumentList.Add(path);
            psi.ArgumentList.Add("-");
            using var p = Process.Start(psi)!;
            var stdout = await p.StandardOutput.ReadToEndAsync();
            await p.WaitForExitAsync();
            return stdout;
        }
        finally
        {
            File.Delete(path);
        }
    }
}
