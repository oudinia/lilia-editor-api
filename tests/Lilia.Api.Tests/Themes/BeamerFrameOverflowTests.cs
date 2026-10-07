using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Blocks;
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
        var opening = B("heading", new { text = "Opening", level = 1 });
        var fits = B("slide", new { title = "Fits", content = "Short." });
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
                opening,
                fits,
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

    // ── reading the log against the source ───────────────────────────────

    private static int LineOf(string latex, string text) =>
        latex.Split('\n').Select((l, i) => (l, i)).First(x => x.l.Contains(text, StringComparison.Ordinal)).i + 1;

    /// <summary>
    /// The test deck as the renderer writes it: the title frame (1), "Fits" (2), "Too long" (3),
    /// "Last" (4), with its block markers.
    /// </summary>
    private static (string Latex, Guid Fits, Guid Long, Guid Last) Source()
    {
        var (heading, fits, tooLong, last) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        var points = string.Join("\n\n", Enumerable.Range(1, 20).Select(i => $"Point {i}."));
        var latex = string.Join("\n",
            @"\documentclass[12pt,a4paper]{beamer}",
            @"% \AtBeginSection[]{\begin{frame}\sectionpage\end{frame}} is only a comment here",
            @"\title{Frame overflow}",
            @"\begin{document}",
            @"\maketitle",
            "",
            $"% block:{heading}",
            @"\section{Opening}",
            $"% block:{fits}",
            @"\label{block-fits}",
            @"\begin{frame}{Fits}",
            "Short.",
            @"\end{frame}",
            $"% block:{tooLong}",
            @"\begin{frame}{Too long}",
            points,
            @"\end{frame}",
            $"% block:{last}",
            @"\begin{frame}{Last}",
            "Short too.",
            @"\end{frame}",
            @"\end{document}");
        return (latex, fits, tooLong, last);
    }

    [Fact]
    public void An_overflow_belongs_to_the_frame_whose_source_holds_its_line_whatever_the_page_markers_say()
    {
        var (latex, _, tooLong, _) = Source();
        var detected = LineOf(latex, "Point 18.");
        var start = LineOf(latex, @"\begin{frame}{Too long}");

        // The old shipout markers, here out of step with the source (a page shipped while TeX
        // was inside the next frame), are not read: only the warning's line is.
        var log = string.Join("\n",
            $"Lilia frame 1 ends on input line {LineOf(latex, @"\maketitle")}",
            "[1",
            "",
            $"Overfull \\vbox (282.10857pt too high) detected at line {detected}",
            " []",
            "",
            $"Lilia frame 2 ends on input line {detected}",
            "[2",
            $"Lilia frame 3 ends on input line {detected + 5}",
            "[3]",
            $"Lilia frame 4 ends on input line {LineOf(latex, "Short too.") + 1}",
            "[4]");

        FrameOverflow.Find(latex, log).Should().Equal(new FrameOverflow.Overflow(3, start));
        FrameOverflow.InDocument(latex, log).Should().Equal([(3, (Guid?)tooLong)]);
        FrameOverflow.Messages(latex, log).Should().Equal(
            "Frame 3 doesn't fit at this theme's size. Split it, or move a block to the next frame.");

        // Detected on the frame's last line, or on the line after it, it is still that frame's.
        var end = LineOf(latex, "Point 20.") + 1;
        FrameOverflow.Find(latex, $"Overfull \\vbox (3.0pt too high) detected at line {end}").Should()
            .Equal(new FrameOverflow.Overflow(3, start));
        FrameOverflow.Find(latex, $"Overfull \\vbox (3.0pt too high) detected at line {end + 1}").Should()
            .Equal(new FrameOverflow.Overflow(3, start));
    }

    [Fact]
    public void A_frame_is_named_once_however_many_overlays_and_frames_come_in_deck_order()
    {
        var (latex, fits, _, last) = Source();
        latex = latex.Replace(@"\begin{frame}{Fits}", @"\begin{frame}<1-3>[label=fits]{Fits}");
        var inFits = LineOf(latex, "Short.");
        var inLast = LineOf(latex, "Short too.");
        var log = string.Join("\n",
            $"Overfull \\vbox (60.3pt too high) detected at line {inLast}",
            $"Overfull \\vbox (43.2pt too high) detected at line {inFits}",
            $"Overfull \\vbox (43.2pt too high) detected at line {inFits}",
            "Overfull \\vbox (12.0pt too high) has occurred while \\output is active",
            $"Overfull \\vbox (43.2pt too high) detected at line {inFits}");

        FrameOverflow.InDocument(latex, log).Should().Equal([(2, (Guid?)fits), (4, (Guid?)last)]);
    }

    [Fact]
    public void Outside_a_deck_or_before_its_frames_an_overflow_names_no_frame()
    {
        var (latex, _, _, _) = Source();
        const string log = "Overfull \\vbox (12.0pt too high) detected at line 15";
        FrameOverflow.Find(latex.Replace("{beamer}", "{article}"), log).Should().BeEmpty();
        FrameOverflow.Find(latex, "Overfull \\vbox (12.0pt too high) detected at line 2").Should().BeEmpty("the preamble");
        FrameOverflow.Find(null, log).Should().BeEmpty();
        FrameOverflow.Find(latex, null).Should().BeEmpty();

        // The title frame is a frame, written by no block.
        FrameOverflow.InDocument(latex, $"Overfull \\vbox (9.0pt too high) detected at line {LineOf(latex, @"\maketitle")}")
            .Should().Equal([(1, (Guid?)null)]);
    }

    [Fact]
    public void Frames_are_numbered_as_beamer_numbers_them()
    {
        var latex = string.Join("\n",
            @"\documentclass{beamer}",
            @"\AtBeginSection[]{\begin{frame}\sectionpage\end{frame}}",
            @"\begin{document}\maketitle",
            @"\section{One}",
            @"\begin{frame}{A}a\end{frame} \begin{frame}[noframenumbering]{Backup}b\end{frame}",
            @"\section*{Aside}",
            @"% \begin{frame}{Commented out}\end{frame}",
            @"\begin{frame}{B}",
            @"\section{Inside a frame is not a divider}",
            @"\end{frame}",
            @"\end{document}",
            @"\begin{frame}{After the end}\end{frame}");
        FrameOverflow.Frames(latex).Should().Equal(
            new FrameOverflow.Frame(1, 3, 3),
            new FrameOverflow.Frame(2, 4, 4),
            new FrameOverflow.Frame(3, 5, 5),
            new FrameOverflow.Frame(3, 5, 5),
            new FrameOverflow.Frame(4, 8, 10));

        // A block validated on its own: the counter is primed with the frames before it.
        var alone = string.Join("\n", @"\documentclass{beamer}", @"\begin{document}", @"\setcounter{framenumber}{2}",
            @"\begin{frame}{Too long}", "x", @"\end{frame}", @"\end{document}");
        FrameOverflow.Frames(alone).Should().Equal(new FrameOverflow.Frame(3, 4, 6));
    }

    [Fact]
    public void A_deck_whose_blocks_arrive_unordered_is_written_in_the_authors_order()
    {
        // The validate route loads the blocks unordered (by key) on the context that renders the
        // deck; the slides compiled shuffled, and the overflowing frame was named 2, 3 or 4.
        var (doc, third) = Deck("cerulean", overflow: true);
        doc.Blocks = doc.Blocks.OrderByDescending(b => b.SortOrder).ToList();
        var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);

        latex.IndexOf("{Fits}", StringComparison.Ordinal).Should().BeLessThan(latex.IndexOf("{Too long}", StringComparison.Ordinal));
        latex.IndexOf("{Too long}", StringComparison.Ordinal).Should().BeLessThan(latex.IndexOf("{Last}", StringComparison.Ordinal));
        var map = LatexLineMap.Parse(latex);
        FrameOverflow.Frames(latex).Single(f => map.BlockAt(f.Start) == third.Id).Number.Should().Be(3);
    }

    [Fact]
    public void The_deck_and_a_block_on_its_own_give_a_frame_the_same_number()
    {
        var blocks = new List<Block>
        {
            B("heading", new { text = "One", level = 1 }),
            B("slide", new { title = "A", content = "a" }),
            B("embed", new { engine = "latex", code = "\\begin{frame}{X}x\\end{frame}\n\\begin{frame}[noframenumbering]{Y}y\\end{frame}\n\\begin{frame}{Z}z\\end{frame}" }),
            B("paragraph", new { text = "between frames" }),
            B("heading", new { text = "Aside", level = 1, numbered = false }),
            B("heading", new { text = "Two", level = 1 }),
            B("slide", new { title = "B", content = "b" }),
        };
        foreach (var dividers in new[] { false, true })
        {
            var doc = new Document
            {
                Id = Guid.NewGuid(), Title = "Numbers", Language = "en", PaperSize = "a4", FontFamily = "serif", FontSize = 11,
                LatexDocumentClass = "beamer", LatexEngine = "pdflatex",
                CustomPreamble = dividers ? @"\AtBeginSection[]{\begin{frame}\sectionpage\end{frame}}" : null,
                Blocks = blocks, BibliographyEntries = new List<BibliographyEntry>(),
            };
            var latex = new RenderService(null!, NullLogger<RenderService>.Instance).RenderToLatex(doc);
            var map = LatexLineMap.Parse(latex);
            var frames = FrameOverflow.Frames(latex);

            foreach (var (block, i) in blocks.Select((b, i) => (b, i)).Where(x => x.b.Type is "slide" or "embed"))
            {
                var first = frames.First(f => map.BlockAt(f.Start) == block.Id);
                first.Number.Should().Be(FrameOverflow.FramesBefore(blocks.Take(i), doc.CustomPreamble) + 1,
                    $"{block.Type} {i} with dividers {dividers}");
            }
            frames.Max(f => f.Number).Should().Be(FrameOverflow.FramesBefore(blocks, doc.CustomPreamble));
        }
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
