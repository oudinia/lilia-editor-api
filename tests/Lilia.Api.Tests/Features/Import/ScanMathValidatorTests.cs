using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Services;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The retest of notebooks A and C after API #678 (21 Nov 2026). Both still stopped:
/// <list type="bullet">
/// <item>A l.604, "Missing $ inserted" at <c>For the unit disc, $\oint_{}$$ $\left</c>, and "\mathrm allowed only in math
///   mode" at <c>\textbackslash, d $\mathrm{y}=$$ iint_{ $\mathrm</c>: two formulas touching make <c>$$</c>, which
///   Lilia's text pairs as a display (TeX does not), and <c>\\iint</c> lost its backslash to the import's line-break pass;</item>
/// <item>C l.515, "Missing $ inserted" at <c>$p\left(z_{-} 0\right)$ not \$0, q\left</c>: the dollar became a price and
///   <c>q\left(</c> was left in text;</item>
/// <item>A l.570: a fraction with an empty part.</item>
/// </list>
/// Each block's text is now made canonical (<see cref="OcrMathText.CanonicalText"/>), rendered and read in TeX's modes
/// (<see cref="OcrMathText.ModeProblem"/>); what still can't compile is printed as written
/// (<see cref="ScanImportCleanup.MakeCompilable"/>). Random concatenations of formula fragments go through the same.
/// </summary>
public class ScanMathValidatorTests : IntegrationTestBase
{
    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);
    private readonly string _userId = $"mathvalid-{Guid.NewGuid():N}"[..28];

    public ScanMathValidatorTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        await using var db = CreateDbContext();
        var pro = db.Plans.Single(p => p.Slug == "pro");
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = _userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    // ── The retest's lines ───────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The lines as the blocks held them (the LaTeX import's text), from the compile log's context.</summary>
    public static TheoryData<string, string> BlockLines => new()
    {
        { "A l.604 (touching, a space between)", @"For the unit disc, $\oint_{}$$ $\left(\frac{1}{2}\right)$ dz = 0." },
        { "A l.604 (touching, iint without its backslash)", @"so that \textbackslash, d $\mathrm{y}=$$ iint_{ $\mathrm{d} x \mathrm{d} y$ } holds." },
        { "C l.515 (escaped dollar, q\\left in text)", @"with $p\left(z_{-} 0\right)$ not \$0, q\left(z_{+} 1\right) here." },
        { "C l.515 (the dollar as the import keeps it)", @"with $p\left(z_{-} 0\right)$ not $0, q\left(z_{+} 1\right) here." },
        { "C l.515 (a formula starting with a number)", @"with $p\left(z_{-} 0\right)$ not $0, q\left(z_{+} 1\right)$ here." },
        { "A l.570 (empty fraction parts)", @"Then $\frac{}{2}$ and $\frac{1}{}$ and $\sqrt{}$." },
        { "touching formulas", @"$a$$b$ and $x$ $$y$$ and $$z$$$w$." },
    };

    [Theory]
    [MemberData(nameof(BlockLines))]
    public void A_line_from_the_retest_renders_as_LaTeX_TeX_reads_and_keeps_its_maths(string name, string line)
    {
        var printed = new List<string>();
        var content = ScanImportCleanup.MakeCompilable(Json(new { text = line }), "paragraph", Render, printed)
            ?? Json(new { text = line });
        var latex = Render.RenderBlockToLatex(B("paragraph", JsonSerializer.Deserialize<JsonElement>(content.RootElement.GetRawText())));
        OcrMathText.ModeProblem(latex).Should().BeNull($"{name}: {latex}");
        printed.Should().BeEmpty($"{name} is made canonical, not printed as written: {latex}");
        latex.Should().Contain("$", "the maths stays maths");
    }

    /// <summary>The fallback for an equation TeX still can't read: its formula in typewriter text. 21 Nov 2026, review: the
    /// backslashes of \text and \texttt had been written as tab characters, and no test reached this path.</summary>
    [Fact]
    public void An_equation_printed_as_written_is_typewriter_text_TeX_reads()
    {
        var latex = ScanImportCleanup.EquationAsText(@"\frac{a}{b \left( x^{");
        latex.Should().StartWith(@"\text{\texttt{").And.EndWith("}}");
        latex.Should().NotContainAny("\t", "\r", "\n", "\f", "\b", "\a", "\v");
        var rendered = Render.RenderBlockToLatex(B("equation", JsonSerializer.Deserialize<JsonElement>(
            JsonSerializer.Serialize(new { latex, equationMode = "display" }))));
        // Compiled by hand (pdflatex, amsmath): no error. ModeProblem is stricter than TeX inside \text{}, so it isn't asked.
        rendered.Should().Contain(@"\text{\texttt{\textbackslash{}frac\{a\}\{b \textbackslash{}left( x\textasciicircum{}\{}}");
    }

    [Fact]
    public void The_canonical_text_of_the_retest_s_lines()
    {
        OcrMathText.CanonicalText(@"For the unit disc, $\oint_{}$$ $\left(\frac{1}{2}\right)$ dz = 0.")
            // As TeX reads it: $ $ is an empty formula, \left(…\right) text, and the last $ is never closed: Lilia prints a $.
            .Should().Be(@"For the unit disc, $\oint_{}  \left(\frac{1}{2}\right)$\$ dz = 0.");
        OcrMathText.CanonicalText(@"with $p\left(z_{-} 0\right)$ not \$0, q\left(z_{+} 1\right) here.")
            .Should().Be(@"with $p\left(z_{-} 0\right)$ not \$0, $q\left(z_{+} 1\right)$ here.");
        // A formula that starts with a number: kept, and written so Lilia can't read "$0," as a price.
        OcrMathText.CanonicalText(@"not $0, q\left(z\right)$ here").Should().Be(@"not ${}0, q\left(z\right)$ here");
        // A dollar never closed is a dollar, as Lilia prints it.
        OcrMathText.CanonicalText(@"costs $ and \alpha").Should().Be(@"costs \$ and $\alpha$");
        OcrMathText.CanonicalText(@"$\frac{}{2}$").Should().Be(@"$\frac{\square}{2}$");
        OcrMathText.CanonicalText(@"$x=\\iint_{D} f$").Should().Be(@"$x=\iint_{D} f$");
        OcrMathText.CanonicalText(@"then iint_{D} f").Should().Be(@"then $\iint_{D} f$");
        OcrMathText.CanonicalText(@"$a$$b$").Should().Be(@"$a b$");
    }

    // ── The check itself ─────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"$a$$b$", "touch")]
    [InlineData(@"$x", "never closed")]
    [InlineData(@"Then \alpha here", @"\alpha in text")]
    [InlineData(@"$\left( x$", @"\left and \right")]
    [InlineData(@"$\right) x$", @"\right before")]
    [InlineData("$$ a $ b", "display closed by one")]
    [InlineData(@"x_1 here", "_ in text")]
    [InlineData(@"{$a}$", "did not open")]
    [InlineData(@"$50% of$", "% in maths")]
    [InlineData(@"$a & b$", "& in a formula")]
    [InlineData("$a\n\nb$", "blank line")]
    [InlineData(@"$\textbackslash x$", @"\textbackslash in maths")]
    [InlineData(@"$\left \frac{a}{b} \right)$", "without a delimiter")]
    [InlineData(@"$\oint_{}_x$", "a second one")]
    [InlineData(@"$\left[^ \right]$", "nothing to sit on")]
    [InlineData(@"$x^\vec{v}$", "nothing to sit on")]
    [InlineData(@"$x\limitsx$", "glued")]
    public void The_check_finds_what_stops_TeX(string latex, string problem) =>
        OcrMathText.ModeProblem(latex).Should().Contain(problem);

    [Theory]
    [InlineData(@"$a$ $b$ and $$c$$ and \(d\) and \[e\]")]
    [InlineData(@"Price \$5 and \ensuremath{\square} and \textbackslash{} and 50\% done.")]
    [InlineData(@"\begin{align}a &= b \\ c &= d\end{align}")]
    [InlineData(@"$\left( \frac{a}{b} \right)^2$ and \url{http://x.org/a_b#c} \label{eq_1}")]
    [InlineData("\\begin{lstlisting}\nx_1 = $5 % c\n\\end{lstlisting}")]
    [InlineData("Text % a comment with $ and _\nmore.")]
    public void The_check_passes_what_TeX_reads(string latex) =>
        OcrMathText.ModeProblem(latex).Should().BeNull();

    // ── Random formula fragments ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Pieces of formulas and text as Mathpix and the import leave them, good and broken.</summary>
    internal static readonly string[] Fragments =
    [
        "$", "$$", @"\$", @"\(", @"\)", @"\[", @"\]", "{", "}", " ", " ", " ", "\n", "\n\n", "the", "q", "x", "0,", "5", "Пусть",
        @"\left(", @"\right)", @"\left\{", @"\right\}", @"\right.", @"\left[", @"\right]", @"\frac{1}{2}", @"\frac{}{x}", @"\frac{a}{}",
        @"\mathrm{d}", @"\mathrm d", @"\textbackslash mathrm\{d\}", @"\textbackslash right)", @"\textbackslash left(", @"\textbackslash qquad",
        @"\textbackslash, ", @"x_{i}", "^2", "_", "^", @"\sum\limits_{i}", @"x\limits", @"\oint_{}", @"\\iint_{D}", "iint_{D}", @"\alpha",
        @"\,", @"\qquad", @"\%", "%", "&", "#", @"\textbf{b}", @"\text{if}", @"\sqrt{2}", @"\vec{v}", @"\int_0^1", @"\'e", @"\^x", "=", "+",
    ];

    internal static string RandomLine(Random random, int pieces) =>
        string.Concat(Enumerable.Range(0, pieces).Select(_ => Fragments[random.Next(Fragments.Length)]));

    [Fact]
    public void Random_formula_fragments_in_a_block_always_render_as_LaTeX_TeX_reads()
    {
        var random = new Random(20261121);
        var failures = new List<string>();
        foreach (var type in new[] { "paragraph", "heading", "list" })
            for (var n = 0; n < 400; n++)
            {
                var line = RandomLine(random, random.Next(1, 14));
                var json = type switch
                {
                    "heading" => Json(new { text = line, level = 2 }),
                    "list" => Json(new { items = new[] { new { text = line } }, listType = "bullet" }),
                    _ => Json(new { text = line }),
                };
                var safe = ScanImportCleanup.MakeCompilable(json, type, Render, []) ?? json;
                var latex = Render.RenderBlockToLatex(B(type, JsonSerializer.Deserialize<JsonElement>(safe.RootElement.GetRawText())));
                if (OcrMathText.ModeProblem(latex) is { } problem) failures.Add($"{type}: {problem}: {line} → {latex}");
            }
        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Random_formula_fragments_as_Mathpix_LaTeX_import_as_blocks_TeX_reads()
    {
        var random = new Random(1121);
        var failures = new List<string>();
        for (var n = 0; n < 300; n++)
        {
            var line = RandomLine(random, random.Next(1, 14));
            var tex = OcrMathText.MathCommandsAsMaths("\\documentclass{article}\n\\begin{document}\n" + line + "\n\\end{document}", out _);
            var doc = await new LatexParser().ParseTextAsync(tex);
            foreach (var p in doc.Elements.OfType<Lilia.Import.Models.ImportParagraph>())
            {
                var json = Json(new { text = p.Text });
                var safe = ScanImportCleanup.MakeCompilable(json, "paragraph", Render, []) ?? json;
                var latex = Render.RenderBlockToLatex(B("paragraph", JsonSerializer.Deserialize<JsonElement>(safe.RootElement.GetRawText())));
                if (OcrMathText.ModeProblem(latex) is { } problem) failures.Add($"{problem}: {line} → {latex}");
            }
        }
        failures.Should().BeEmpty();
    }

    // ── Through the bulk placement, compiled for real ────────────────────────────────────────────────────────────

    /// <summary>The same lines as Mathpix's LaTeX for the pages (before the import).</summary>
    internal static readonly string[] SourceLines =
    [
        @"For the unit disc, $\oint_{}$$ $\left(\frac{1}{2}\right)$ dz = 0.",
        @"so that \textbackslash , d $\mathrm{y}=$$\\iint_{D} \mathrm{d} x \mathrm{d} y$ holds.",
        @"so that d $\mathrm{y}=$ $ \\iint_{ D} f$ holds.",
        @"with $p\left(z_{-} 0\right)$ not \$0, q\left(z_{+} 1\right) here.",
        @"with $p\left(z_{-} 0\right)$ not $0, q\left(z_{+} 1\right)$ here.",
        @"Then $\frac{}{2}$ and $\frac{1}{}$.",
    ];

    private async Task<(IServiceScope Scope, Guid DocumentId, Guid SessionId)> PlaceAsync(string body)
    {
        var documentId = Guid.NewGuid();
        var placeholder = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO documents (id, owner_id, title, created_at, updated_at)
                VALUES ({documentId}, {_userId}, {"Formula fragments"}, {DateTime.UtcNow}, {DateTime.UtcNow})");
            db.Blocks.Add(new Block
            {
                Id = placeholder, DocumentId = documentId, Type = "paragraph", SortOrder = 0,
                Content = JsonDocument.Parse("""{"text":"Pages 1–10: being read…"}"""),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var id = MathpixTexZipTests.PdfId;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry($"{id}/{id}.tex").Open(), Encoding.UTF8))
            w.Write("\\documentclass[10pt]{article}\n\\usepackage{amsmath}\n\\usepackage{amssymb}\n\\begin{document}\n\n" + body + "\n\\end{document}\n");
        var scope = Fixture.Factory.Services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IPdfImportPipeline>();
        var placed = await pipeline.PlaceLatexProjectAsync(_userId, "notebook.pdf", documentId, placeholder, applyPreamble: true, ms.ToArray(), null);
        return (scope, documentId, placed.SessionId);
    }

    [Fact]
    public async Task The_retest_s_lines_placed_render_as_LaTeX_TeX_reads()
    {
        var (scope, documentId, _) = await PlaceAsync(string.Join("\n\n", SourceLines));
        using var _s = scope;
        await using var db = CreateDbContext();
        var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId).OrderBy(b => b.SortOrder).ToListAsync();
        foreach (var b in blocks)
            OcrMathText.ModeProblem(Render.RenderBlockToLatex(b)).Should().BeNull(b.Content.RootElement.GetRawText());
        string.Concat(blocks.Select(b => b.Content.RootElement.GetRawText())).Should().Contain(@"\\iint").And.Contain(@"q\\left(");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_the_retest_s_lines_and_random_formula_fragments_compile_with_no_LaTeX_error()
    {
        var random = new Random(211121);
        var lines = SourceLines.Concat(Enumerable.Range(0, 200).Select(_ => RandomLine(random, random.Next(1, 14))));
        var (scope, documentId, _) = await PlaceAsync(string.Join("\n\n", lines));
        using var _s = scope;
        var exports = scope.ServiceProvider.GetRequiredService<IDocumentExportService>();
        var (pdf, engine, problems) = await exports.ExportToPdfWithProblemsAsync(documentId, "latex", null, false);
        PdfPages.LooksLikePdf(pdf).Should().BeTrue();
        problems.Should().BeEmpty($"every fragment compiles or is printed as written ({engine})");
    }

    private static JsonDocument Json(object o) => JsonDocument.Parse(JsonSerializer.Serialize(o));
}
