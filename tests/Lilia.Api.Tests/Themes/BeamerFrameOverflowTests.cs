using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Engines;
using Microsoft.Extensions.Logging.Abstractions;

namespace Lilia.Api.Tests.Themes;

/// <summary>
/// A frame that doesn't fit is named in validation (Olivia, 6 Oct 2026): "Keep the 14 pt floor,
/// but don't let an overflow go unnoticed." The themes never shrink a frame; validation says
/// "Frame N doesn't fit at this theme's size. Split it, or move a block to the next frame." and
/// links it to the slide block. Compiled for real under each beamer theme, and read from logs.
///
/// <para>In the theme-availability collection: the real kpsewhich probe decides here, and the
/// unit tests that override it must not run at the same time.</para>
/// </summary>
[Collection(ThemeAvailabilityCollection.Name)]
public class BeamerFrameOverflowTests
{
    private static int _order;

    private static Block B(string type, object content) => new()
    {
        Id = Guid.NewGuid(),
        Type = type,
        SortOrder = _order++,
        Content = JsonDocument.Parse(JsonSerializer.Serialize(content)),
    };

    /// <summary>Twenty short paragraphs: more than any theme's frame holds at 14 pt.</summary>
    internal static readonly string TooLong =
        string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Point {i} of a frame that holds too much."));

    /// <summary>
    /// The title frame (1), "Fits" (2), "Too long" (3) or "Also short" (3), "Last" (4). Returns
    /// the deck and its third frame's block.
    /// </summary>
    private static (Document Doc, Block Third) Deck(string theme, bool overflow)
    {
        var third = B("slide", overflow
            ? new { title = "Too long", content = TooLong }
            : new { title = "Also short", content = "Two lines.\n\nNo more." });
        var doc = new Document
        {
            Id = Guid.NewGuid(), Title = "Frame overflow", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
            LatexDocumentClass = "beamer", LatexEngine = "pdflatex",
            Look = JsonSerializer.Serialize(new { theme }),
            Blocks =
            [
                B("heading", new { text = "Opening", level = 1 }),
                B("slide", new { title = "Fits", content = "Short." }),
                third,
                B("heading", new { text = "Closing", level = 1 }),
                B("slide", new { title = "Last", content = "Short too." }),
            ],
            BibliographyEntries = new List<BibliographyEntry>(),
        };
        return (doc, third);
    }

    private static async Task<(string Latex, LatexValidationResult Result)> Validate(Document doc)
    {
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
        latex.Should().Contain(FrameOverflow.Marker, "a deck's preview and validation compiles log each frame");
        var result = await new LaTeXRenderService(NullLogger<LaTeXRenderService>.Instance).ValidateAsync(latex, "pdflatex");
        result.Valid.Should().BeTrue(result.Error);
        return (latex, result);
    }

    [Theory]
    [InlineData("cerulean")]
    [InlineData("index")]
    [InlineData("exposition")]
    [InlineData("classic")]
    public async Task A_frame_that_does_not_fit_is_named_and_linked_to_its_slide(string theme)
    {
        var (doc, third) = Deck(theme, overflow: true);
        var (latex, result) = await Validate(doc);

        result.Warnings.Should().Contain(
            "Frame 3 doesn't fit at this theme's size. Split it, or move a block to the next frame.");
        result.Warnings.Should().NotContain(w => w.StartsWith("Content is too tall for the page", StringComparison.Ordinal),
            "in a deck the overflow is a frame, named, not a page");
        result.Warnings.Count(w => w.StartsWith("Frame ", StringComparison.Ordinal)).Should().Be(1, "only frame 3 overflows");

        FrameOverflow.InDocument(latex, result.Log).Should().Equal([(3, (Guid?)third.Id)]);
    }

    [Theory]
    [InlineData("cerulean")]
    [InlineData("index")]
    [InlineData("exposition")]
    public async Task A_deck_that_fits_reports_nothing(string theme)
    {
        var (doc, _) = Deck(theme, overflow: false);
        var (latex, result) = await Validate(doc);

        result.Warnings.Should().NotContain(w => w.Contains("doesn't fit", StringComparison.Ordinal)
                                               || w.Contains("too tall", StringComparison.Ordinal));
        FrameOverflow.InDocument(latex, result.Log).Should().BeEmpty();
    }

    [Fact]
    public void The_export_does_not_carry_the_frame_marker()
    {
        var (doc, _) = Deck("cerulean", overflow: true);
        var tex = new LaTeXExportService(null!, null!, NullLogger<LaTeXExportService>.Instance)
            .BuildSingleFileLatex(doc, doc.Blocks.ToList(), [], new LaTeXExportOptions { CompileHere = true });
        tex.Should().NotContain("Lilia frame");
    }

    [Fact]
    public void An_article_carries_no_frame_marker()
    {
        var (doc, _) = Deck("classic", overflow: false);
        doc.LatexDocumentClass = "article";
        doc.Blocks = doc.Blocks.Where(b => b.Type != "slide").ToList();
        new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc).Should().NotContain("Lilia frame");
        RenderService.BuildPreambleForValidation(doc, LatexEngine.Pdflatex).Should().NotContain("Lilia frame");
    }

    // ── reading the log ──────────────────────────────────────────────────

    [Fact]
    public void The_marker_after_an_overflow_names_its_frame_once_however_many_overlays()
    {
        const string log = """
            Lilia frame 1 ends on input line 120
            [1
            Overfull \vbox (43.24336pt too high) detected at line 131
             []

            Lilia frame 2 ends on input line 131
            [2]
            Lilia frame 3 ends on input line 140
            [3]
            Overfull \vbox (60.3pt too high) detected at line 152
            Lilia frame 4 ends on input line 152
            [4]
            Overfull \vbox (60.3pt too high) detected at line 152
            Lilia frame 4 ends on input line 152
            [5]
            """;
        FrameOverflow.Find(log).Should().Equal(new FrameOverflow.Overflow(2, 131), new FrameOverflow.Overflow(4, 152));
        FrameOverflow.Messages(log).Should().Equal(
            "Frame 2 doesn't fit at this theme's size. Split it, or move a block to the next frame.",
            "Frame 4 doesn't fit at this theme's size. Split it, or move a block to the next frame.");
    }

    [Fact]
    public void Without_markers_an_overflow_names_no_frame()
    {
        FrameOverflow.Find("Overfull \\vbox (12.0pt too high) detected at line 9\n[1]").Should().BeEmpty();
        FrameOverflow.Find(null).Should().BeEmpty();
    }

    [Fact]
    public void Frames_before_a_block_count_the_title_slides_embedded_frames_and_dividers()
    {
        var blocks = new[]
        {
            B("heading", new { text = "One", level = 1 }),
            B("slide", new { title = "A", content = "a" }),
            B("embed", new { engine = "latex", code = "\\begin{frame}{X}x\\end{frame}\n\\begin{frame}{Y}y\\end{frame}" }),
            B("paragraph", new { text = "between frames" }),
            B("heading", new { text = "Aside", level = 1, numbered = false }),
            B("heading", new { text = "Sub", level = 2 }),
        };
        FrameOverflow.FramesBefore([], null).Should().Be(1, "the title frame");
        FrameOverflow.FramesBefore(blocks, null).Should().Be(4, "title, a slide and an embed's two frames");
        FrameOverflow.FramesBefore(blocks, @"\AtBeginSection[]{\begin{frame}\sectionpage\end{frame}}")
            .Should().Be(5, "and the numbered section's divider");
    }
}
