using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Core.Entities;
using Lilia.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Lilia.Engines;

namespace Lilia.Api.Tests.Export;

/// <summary>
/// P1.2 — the block-type x emitter coverage matrix.
///
/// One block model, several parallel dispatches over it. Adding a block type
/// means touching every one of them, and nothing enforced that you did: an
/// emitter with no arm for a type falls to its default, which produces
/// syntactically valid output with the content gone. That is the same silent
/// failure P0.3 fixed for one arm of one emitter.
///
/// <para><b>Determined by running the emitters, not by regexing their source.</b>
/// <c>ExportHandlerCoverageTests</c> takes the source-text approach for the two
/// dispatches that are private, and it is worth being explicit about why this
/// file does not: the first attempt at measuring this drift matched
/// <c>"heading" or "header" =></c> against only its last alternative and
/// reported that LML could not emit headings. The finding was false and looked
/// alarming enough to check. Calling the emitter cannot lie about what the
/// emitter does.</para>
///
/// The gate is <see cref="Coverage_matrix_has_not_drifted"/>: it compares the
/// live matrix against <see cref="KnownGaps"/> and prints the whole thing on
/// failure. Add a block type and forget an emitter, and it fails the day the
/// constant lands — naming the type and the emitter.
/// </summary>
public class EmitterCoverageMatrixTests
{
    // The block renderers are pure; RenderService only needs a context for
    // other work. One in-memory instance serves every cell in the matrix.
    private static readonly RenderService Render = BuildRenderService();
    private static readonly TypstRenderService TypstRender = BuildTypstService();

    private static RenderService BuildRenderService()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new RenderService(new LiliaDbContext(opts), NullLogger<RenderService>.Instance);
    }

    private static TypstRenderService BuildTypstService()
    {
        var opts = new DbContextOptionsBuilder<LiliaDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new TypstRenderService(new LiliaDbContext(opts), NullLogger<TypstRenderService>.Instance);
    }

    /// <summary>
    /// An emitter, and how to recognise its "I have no arm for this" output.
    /// Each marker is the literal default arm of that dispatch — see the
    /// referenced line in the comment beside it.
    /// </summary>
    private sealed record Emitter(
        string Name,
        Func<Block, string> Emit,
        Func<string, string, bool> FellToDefault);

    private static readonly Emitter[] Emitters =
    [
        // RenderService.cs — `_ => $"% Unknown block type: {block.Type}"`
        new("latex", b => Render.RenderBlockToLatex(b),
            (output, _) => output.Contains("% Unknown block type:", StringComparison.Ordinal)),

        // RenderService.cs — an empty styled div carrying only the type name.
        new("html", b => Render.RenderBlockToHtml(b),
            (output, type) => output.Trim() == $"<div class=\"block block-{type}\"></div>"),

        // RenderService.Markdown.cs — `<!-- Unsupported block type: … -->`
        new("markdown", b => Render.RenderBlockToMarkdown(b),
            (output, _) => output.Contains("Unsupported block type:", StringComparison.Ordinal)),

        // RenderService.Lml.cs — the honest default P0.3 installed.
        new("lml", b => Render.RenderBlockToLml(b),
            (output, _) => output.Contains("not supported in LML", StringComparison.Ordinal)),

        // TypstRenderService.cs — `_ => $"// Unknown block type: {block.Type}"`
        new("typst", b => TypstRender.RenderBlockToTypst(b),
            (output, _) => output.Contains("// Unknown block type:", StringComparison.Ordinal)),
    ];

    private const string Latex = "latex";
    private const string Html = "html";
    private const string Markdown = "markdown";
    private const string Lml = "lml";
    private const string Typst = "typst";
    private static readonly string[] AllEmitters = [Latex, Html, Markdown, Lml, Typst];

    /// <summary>
    /// Cells that are legitimately not covered, as "blockType:emitter".
    ///
    /// This set is the reviewed part of this test. Every entry is a decision
    /// that the gap is acceptable — not a record of what the code happens to do
    /// today. A new entry belongs here only with a reason.
    ///
    /// Measured 2026-07-30: 90 gaps, in four groups. Three were expected. The
    /// fourth — columnLayout in Typst — was not, and is the finding this test
    /// paid for.
    /// </summary>
    private static readonly HashSet<string> KnownGaps = BuildKnownGaps();

    private static HashSet<string> BuildKnownGaps()
    {
        var gaps = new HashSet<string>(StringComparer.Ordinal);

        void Gap(string[] types, params string[] emitters)
        {
            foreach (var type in types)
                foreach (var emitter in emitters)
                    gaps.Add($"{type}:{emitter}");
        }

        // ── ePub verticals ────────────────────────────────────────────────
        // EpubService has its own dispatch and its own output format. These
        // types never reach a general document export, so an arm in each of the
        // five general emitters would be dead code.
        Gap(
            [
                BlockTypes.FrontMatter, BlockTypes.BackMatter, BlockTypes.Verse,
                BlockTypes.Aside, BlockTypes.Annotation, BlockTypes.Cover,
                BlockTypes.ChapterBreak,
            ],
            AllEmitters);

        // ── Invoice verticals ─────────────────────────────────────────────
        // Same argument: the inv-* namespace is a separate pipeline with its own
        // renderer, and out of scope for articles, reports and books.
        Gap(
            [
                BlockTypes.InvHeader, BlockTypes.InvParty, BlockTypes.InvLineItems,
                BlockTypes.InvTaxSummary, BlockTypes.InvTotals, BlockTypes.InvPayment,
                BlockTypes.InvAllowanceCharge, BlockTypes.InvDelivery, BlockTypes.InvNote,
            ],
            AllEmitters);

        // ── CV types — de-scoped, not broken ──────────────────────────────
        // Scope is articles, reports and books (P1.5); CV is 1.7% of the corpus
        // and falling, and the blocks are already hidden from the insert
        // surfaces. LaTeX and HTML DO emit them, so documents that already
        // contain CV blocks keep rendering and keep exporting to PDF — which is
        // exactly why the blocks were hidden rather than removed.
        //
        // This is the drift PLAN.md P1.2 predicted: "five of those six vanish
        // the moment CV and slides are de-scoped". The sixth was `title`, and
        // it is no longer in this list — P0.3 closed it.
        Gap(
            [BlockTypes.PersonalInfo, BlockTypes.Photo, BlockTypes.CvEntry, BlockTypes.CvSection],
            Markdown, Lml, Typst);

        // ── columnLayout in Typst — the one that matters ──────────────────
        // NOT a vertical and NOT de-scoped. columnLayout is a general layout
        // block that LaTeX, HTML, Markdown and LML all emit, and Typst does not
        // — and Typst is the DEFAULT PDF engine (pdfEngine = "auto"). So a
        // multi-column block in a document exported on default settings reaches
        // TypstRenderService, matches no arm, and becomes a `// Unknown block
        // type` comment: the columns silently do not happen.
        //
        // Listed rather than fixed because writing the Typst arm is P3.1 work,
        // not this branch's. Recorded here so it cannot be forgotten, and
        // written up in lilia-docs/plan/notebook.md.
        Gap([BlockTypes.ColumnLayout], Typst);

        return gaps;
    }

    private static Block BlockOf(string type) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        Type = type,
        SortOrder = 0,
        // The dispatch keys on Type alone, so empty content still reaches the
        // right arm. An arm that cannot survive empty content is a separate
        // defect and shows up below as "throws".
        Content = JsonDocument.Parse("{}"),
    };

    private static string Cell(Emitter emitter, string type)
    {
        var block = BlockOf(type);
        try
        {
            var output = emitter.Emit(block);
            return emitter.FellToDefault(output ?? string.Empty, type) ? "gap" : "ok";
        }
        catch (Exception ex)
        {
            return $"throws:{ex.GetType().Name}";
        }
    }

    private static (HashSet<string> Gaps, HashSet<string> Throws, string Report) BuildMatrix()
    {
        var gaps = new HashSet<string>(StringComparer.Ordinal);
        var throws = new HashSet<string>(StringComparer.Ordinal);
        var report = new StringBuilder();

        report.AppendLine();
        report.Append("block type".PadRight(22));
        foreach (var e in Emitters) report.Append(e.Name.PadRight(10));
        report.AppendLine();
        report.AppendLine(new string('-', 22 + Emitters.Length * 10));

        foreach (var type in BlockTypes.All.OrderBy(t => t, StringComparer.Ordinal))
        {
            report.Append(type.PadRight(22));
            foreach (var e in Emitters)
            {
                var cell = Cell(e, type);
                report.Append(cell.PadRight(10));
                var key = $"{type}:{e.Name}";
                if (cell == "gap") gaps.Add(key);
                else if (cell.StartsWith("throws", StringComparison.Ordinal)) throws.Add($"{key} ({cell})");
            }
            report.AppendLine();
        }

        return (gaps, throws, report.ToString());
    }

    [Fact]
    public void Coverage_matrix_has_not_drifted()
    {
        var (gaps, throws, report) = BuildMatrix();

        var appeared = gaps.Except(KnownGaps).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var closed = KnownGaps.Except(gaps).OrderBy(k => k, StringComparer.Ordinal).ToArray();

        // Asserted first: a crash is worse than a gap. A gap loses content, a
        // throw takes the whole export down with it.
        throws.Should().BeEmpty(
            "an emitter threw on a canonical block type rather than emitting "
            + "anything. Empty content is not a reason to crash.\nMatrix:{0}", report);

        appeared.Should().BeEmpty(
            "a block type reached an emitter with no arm for it, so it exported as "
            + "valid-looking output with the content gone. Either add the arm, or add "
            + "the cell to KnownGaps with a reason.\nNew gaps: {0}\nMatrix:{1}",
            string.Join(", ", appeared), report);

        closed.Should().BeEmpty(
            "these cells are listed in KnownGaps but are now covered — delete them "
            + "from the set so it keeps meaning what it says.\nMatrix:{0}", report);
    }

    /// <summary>
    /// The four emitters that must never lose a core content block, asserted
    /// individually so a failure names the emitter rather than the matrix.
    /// Deliberately excludes verticals (inv-*, epub, cv) and Typst, which is a
    /// PDF engine rather than a shipped export format.
    /// </summary>
    public static IEnumerable<object[]> CoreTypesAndEmitters() =>
        from type in new[]
        {
            BlockTypes.Paragraph, BlockTypes.Heading, BlockTypes.Equation,
            BlockTypes.Figure, BlockTypes.Table, BlockTypes.Code, BlockTypes.List,
            BlockTypes.Blockquote, BlockTypes.Theorem, BlockTypes.Abstract,
            BlockTypes.Title, BlockTypes.Bibliography, BlockTypes.TableOfContents,
            BlockTypes.PageBreak,
        }
        from emitter in new[] { "latex", "html", "markdown", "lml" }
        select new object[] { type, emitter };

    [Theory]
    [MemberData(nameof(CoreTypesAndEmitters))]
    public void Core_content_blocks_are_emitted_everywhere(string type, string emitterName)
    {
        var emitter = Emitters.Single(e => e.Name == emitterName);
        Cell(emitter, type).Should().Be("ok",
            $"'{type}' is core document content and '{emitterName}' must not fall to "
            + "its default for it — that exports structurally valid output with the "
            + "content missing, which is the failure this plan exists to remove.");
    }
}
