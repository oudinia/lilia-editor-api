using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Engines.Themes.DocumentThemeCompileTests;

namespace Lilia.Api.Tests.Engines.Themes;

/// <summary>
/// The launch bot's #117 (16 Nov 2026; Oussama: "fix them"): a deck's blocks were written outside any frame, so an
/// imported deck's frame titles (imported as unnumbered headings) printed nowhere. A heading followed by content opens a
/// frame titled with it now.
/// </summary>
public class BeamerFramesTests
{
    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(), Type = type, SortOrder = _order++, Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    private static Document Deck(params Block[] blocks) => new()
    {
        Id = Guid.NewGuid(), Title = "Deck", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
        LatexDocumentClass = "beamer", Blocks = blocks.ToList(), BibliographyEntries = new List<BibliographyEntry>(),
    };

    private static string Preview(Document doc) => new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

    private static string Export(Document doc) => new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
        .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });

    private static Document Sample() => Deck(
        B("paragraph", new { text = "Opening words." }),
        B("heading", new { text = "Outline", level = 1 }),
        B("list", new { items = new[] { "One", "Two" }, ordered = true }),
        B("heading", new { text = "Method", level = 1, numbered = false }),
        B("paragraph", new { text = "We measure." }),
        B("code", new { code = "print(1)", language = "python" }));

    [Fact]
    public void Each_heading_with_content_opens_a_frame_titled_with_it()
    {
        foreach (var tex in new[] { Preview(Sample()), Export(Sample()) })
        {
            var body = tex[tex.IndexOf(@"\begin{document}", StringComparison.Ordinal)..];
            body.Should().Contain(@"\section{Outline}", "a numbered heading keeps its place in the navigation");
            body.Should().Contain(@"\frametitle{Outline}").And.Contain(@"\frametitle{Method}");
            body.Should().NotContain(@"\section*{Method}", "an unnumbered heading is only the frame's title");
            // The opening paragraph has its own untitled frame, before the first heading.
            body.IndexOf("Opening words.", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf(@"\section{Outline}", StringComparison.Ordinal));
            body.Split(@"\begin{frame}").Length.Should().Be(4, "three frames: the opening, Outline, Method");
            body.Split(@"\begin{frame}").Length.Should().Be(body.Split(@"\end{frame}").Length);
        }
    }

    [Fact]
    public void A_frame_with_code_is_fragile()
    {
        var body = Preview(Sample());
        var method = body[body.LastIndexOf(@"\begin{frame}", StringComparison.Ordinal)..];
        method.Should().StartWith(@"\begin{frame}[fragile]\frametitle{Method}");
    }

    [Fact]
    public void A_heading_with_nothing_under_it_is_a_section_only()
    {
        var body = Preview(Deck(B("heading", new { text = "Part One", level = 1 }), B("heading", new { text = "First", level = 1, numbered = false }), B("paragraph", new { text = "Text." })));
        body.Should().Contain(@"\section{Part One}").And.NotContain(@"\frametitle{Part One}").And.Contain(@"\frametitle{First}");
    }

    [Fact]
    public void An_article_is_unchanged() =>
        Preview(new Document { Id = Guid.NewGuid(), Title = "A", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11, LatexDocumentClass = "article",
            Blocks = [B("heading", new { text = "Intro", level = 1 }), B("paragraph", new { text = "Text." })], BibliographyEntries = new List<BibliographyEntry>() })
            .Should().NotContain(@"\begin{frame}");

    [Fact]
    public async Task An_imported_frame_title_is_an_unnumbered_heading()
    {
        var doc = await new LatexParser().ParseTextAsync("\\documentclass{beamer}\\begin{document}\\begin{frame}{Results}Ours is faster.\\end{frame}\\end{document}");
        var heading = doc.Elements.OfType<ImportHeading>().Single();
        heading.Text.Should().Contain("Results");
        heading.Numbered.Should().BeFalse();
    }

    [Fact]
    public async Task The_PDF_prints_each_frame_title()
    {
        var pdf = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).RenderToPdfAsync(Preview(Sample()), "pdflatex", timeout: 120);
        var text = await Tool("pdftotext", pdf, "-");
        text.Should().Contain("Outline").And.Contain("Method").And.Contain("We measure.").And.Contain("print");
    }
}
