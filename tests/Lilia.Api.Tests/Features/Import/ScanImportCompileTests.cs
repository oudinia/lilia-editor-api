using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Core.Entities;
using Lilia.Engines;
using Lilia.Import.Models;
using Lilia.Import.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Lilia.Api.Tests.Features.Export.Publishers.PublisherClassTests;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's bulk import of three handwritten notebooks (21 Nov 2026, API #675): Forkosh's page, Wikimedia notes and
/// Russian lecture notes from Rostov. Every document was placed, and none compiled:
/// <list type="number">
/// <item>Cyrillic text went to pdflatex (<c>\section*{Nuner. snep плоском \&amp;}</c>, "Unicode character п not set up"):
///   the language read from the text stayed English, since the Cyrillic was under 30 % of its letters
///   (<see cref="UnicodeFacesText"/>);</item>
/// <item>a text accent in maths (<c>$\'e$</c>, "Please use \mathaccent for accents in math mode"; <see cref="OcrMathAccents"/>);</item>
/// <item>a maths command in running text: <c>\textbackslash{}mathrm</c> printed, and a bare <c>\frac</c> or <c>\alpha</c>
///   was mangled or stopped the PDF (<see cref="OcrMathText"/>).</item>
/// </list>
/// Fixtures reproduce Mathpix's shapes; the RequiresTeX tests compile for real.
/// </summary>
public class ScanImportCompileTests : IntegrationTestBase
{
    private static readonly RenderService Render = new(null!, NullLogger<RenderService>.Instance);
    private readonly string _userId = $"scancompile-{Guid.NewGuid():N}"[..28];

    public ScanImportCompileTests(TestDatabaseFixture fixture) : base(fixture) { }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        await using var db = CreateDbContext();
        var pro = db.Plans.Single(p => p.Slug == "pro");
        db.UserPlans.Add(new UserPlan { Id = Guid.NewGuid(), UserId = _userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    // ── 1. Cyrillic ───────────────────────────────────────────────────────────────────────────────────────────────

    private const string CyrillicTitle = @"Nuner. snep плоском \&";
    private const string CyrillicParagraph = "Пусть дана плоскость P and a point A on it; the notes then continue in English for a while, with the proof that follows.";

    [Theory]
    [InlineData(@"\section*{Nuner. snep плоском \&}", LatexEngine.Lualatex)]
    [InlineData("Пусть дана плоскость.", LatexEngine.Lualatex)]
    [InlineData("Polytonic Greek: λόγος.", LatexEngine.Lualatex)]
    [InlineData("The γ rays and α = 0.05 keep pdflatex: the preamble maps them onto maths.", LatexEngine.Pdflatex)]
    [InlineData("Plain English text.", LatexEngine.Pdflatex)]
    public void Cyrillic_or_unmapped_Greek_needs_LuaLaTeX(string latex, LatexEngine engine) =>
        EngineDetector.Detect(latex).Should().Be(engine);

    private static Document MixedDocument() => new()
    {
        Id = Guid.NewGuid(),
        Title = "Lecture notes",
        Language = "auto",
        Blocks =
        [
            B("heading", new { text = CyrillicTitle, level = 1 }),
            B("paragraph", new { text = "These are the notes of a course read in the spring, written by hand and scanned. The theorem below is the one the lecture proves, and the proof is given in full in the second part of the notes." }),
            B("paragraph", new { text = CyrillicParagraph }),
        ],
    };

    [Fact]
    public void A_mostly_English_document_with_Cyrillic_stays_English_and_gets_LuaLaTeX_with_Unicode_faces()
    {
        var doc = MixedDocument();
        DocumentLanguage.Detect(doc).Should().Be("en", "the Cyrillic is under 30 % of the letters: that is why it stayed on pdflatex");

        var plan = DocumentLanguage.PlanFor(doc, null, LatexEngine.Pdflatex, hasCjk: false);
        plan.Engine.Should().Be(LatexEngine.Lualatex);
        plan.Spec!.Code.Should().Be("en", "the language is still the one the text reads as");

        var latex = Render.RenderToLatex(doc);
        latex.Should().Contain(@"\usepackage{fontspec}").And.Contain("cmunrm.otf", "Computer Modern Unicode has the Cyrillic letters")
            .And.NotContain(@"\usepackage[T1]{fontenc}");
        latex.Should().Contain("плоском");
    }

    [Fact]
    public void Cyrillic_words_in_maths_are_set_as_text_and_Cyrillic_outside_maths_is_never_touched()
    {
        var replaced = new List<string>();
        OcrMathGuard.InText(@"плоском: $v_{нач} = F_{тр} + \text{сила}$", replaced)
            .Should().Be(@"плоском: $v_{\text{нач}} = F_{\text{тр}} + \text{сила}$");
        OcrMathGuard.InMath(@"F_{сила трения} = \mu N", replaced).Should().Be(@"F_{\text{сила трения}} = \mu N");
        replaced.Should().BeEmpty("a Cyrillic word prints as text: nothing is boxed");
    }

    // ── 2. Text accents in maths ─────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"$\'e$ and $\^x$", @"$\acute{e}$ and $\hat{x}$")]
    [InlineData(@"\( \""u + \~n + \=a + \.x + \`e \)", @"\( \ddot{u} + \tilde{n} + \bar{a} + \dot{x} + \grave{e} \)")]
    [InlineData(@"$\u{a} \v c \r{a} \'\i$", @"$\breve{a} \check{c} \mathring{a} \acute{\imath}$")]
    [InlineData(@"$\H{o} + \c{c}$", @"$\text{\H{o}} + \text{\c{c}}$")]
    [InlineData(@"$\'{et}$", @"$\text{\'{et}}$")]
    [InlineData(@"$x = \text{caf\'e}$ and caf\'e outside", @"$x = \text{caf\'e}$ and caf\'e outside")]
    [InlineData(@"$\vec{v} \cdot \bar{x} \hat{y}$ \\'", @"$\vec{v} \cdot \bar{x} \hat{y}$ \\'")]
    public void A_text_accent_in_maths_becomes_its_maths_accent(string text, string expected)
    {
        var uncertain = new List<string>();
        OcrMathAccents.InText(text, uncertain).Should().Be(expected);
        uncertain.Should().BeEmpty("each prints the same mark over the same letter");
    }

    [Fact]
    public void In_an_equation_every_accent_is_maths_and_only_one_with_nothing_under_it_is_reported()
    {
        var uncertain = new List<string>();
        OcrMathAccents.InMath(@"\'e + \^x_1 = a \\ f\'", uncertain).Should().Be(@"\acute{e} + \hat{x}_1 = a \\ f'");
        uncertain.Should().Equal(@"\'");
    }

    // ── 3. Maths commands in text ────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"Then \mathrm{d}x is small.", @"Then $\mathrm{d}x$ is small.")]
    [InlineData(@"Then \textbackslash mathrm\{d\} x is small.", @"Then $\mathrm{d} x$ is small.")]
    [InlineData(@"Then \textbackslash{}mathrm\{d\}x and \textbackslash frac\{1\}\{2\}.", @"Then $\mathrm{d}x$ and $\frac{1}{2}$.")]
    [InlineData(@"Half is \frac{1}{2}, the angle \alpha, the sum \sum_{i=1}^{n} x_i.", @"Half is $\frac{1}{2}$, the angle $\alpha$, the sum $\sum_{i=1}^{n} x_i$.")]
    [InlineData(@"Spaced \mathrm {d} x and \sqrt[3]{8}.", @"Spaced $\mathrm {d} x$ and $\sqrt[3]{8}$.")]
    [InlineData(@"Maths stays: \( \mathrm{d}x \), $\alpha$, $$\frac{1}{2}$$ and \[ \sum x \].", @"Maths stays: \( \mathrm{d}x \), $\alpha$, $$\frac{1}{2}$$ and \[ \sum x \].")]
    [InlineData(@"Text commands stay: \textbf{bold}, \ldots, \textbackslash section and 50\% of \emph{it}.", @"Text commands stay: \textbf{bold}, \ldots, \textbackslash section and 50\% of \emph{it}.")]
    public void A_maths_command_in_text_becomes_inline_maths(string text, string expected) =>
        OcrMathText.MathCommandsAsMaths(text, out _).Should().Be(expected);

    [Fact]
    public void Only_the_body_is_read_not_the_preamble()
    {
        var tex = "\\documentclass{article}\n\\newcommand{\\half}{\\frac{1}{2}}\n\\begin{document}\nA \\alpha.\n\\end{document}";
        OcrMathText.MathCommandsAsMaths(tex, out var n).Should().Be(tex.Replace("A \\alpha.", "A $\\alpha$."));
        n.Should().Be(1);
    }

    [Fact]
    public async Task Read_by_the_LaTeX_import_the_maths_command_is_maths_not_textbackslash()
    {
        var tex = OcrMathText.MathCommandsAsMaths(NotesTex, out _);
        var doc = await new LatexParser().ParseTextAsync(tex);
        var paragraphs = doc.Elements.OfType<ImportParagraph>().Select(p => p.Text).ToList();
        var line = paragraphs.Single(p => p.Contains("outside maths"));
        line.Should().Contain(@"$\mathrm{d}x$").And.Contain(@"$\mathrm{d} y$").And.Contain(@"$\frac{1}{2}$").And.Contain(@"$\alpha$");
        var rendered = Render.RenderBlockToLatex(B("paragraph", new { text = line }));
        rendered.Should().NotContain(@"\textbackslash").And.NotContain("1{2}");
    }

    // ── The imports, end to end ──────────────────────────────────────────────────────────────────────────────────

    /// <summary>A Mathpix tex.zip as the Rostov notes gave: Cyrillic, accents in maths, maths commands in the text.</summary>
    internal const string NotesTex = """
        \documentclass[10pt]{article}
        \usepackage[utf8]{inputenc}
        \usepackage[T1]{fontenc}
        \usepackage{amsmath}
        \usepackage{amsfonts}
        \usepackage{amssymb}

        \begin{document}
        \section*{Nuner. snep плоском \&}
        Пусть дана плоскость $P$ и точка $A$ на ней, и сила $F_{трения}$. The notes go on in English: the length is $\'e$, the estimate is $\^x$.

        The derivative is written \mathrm{d}x outside maths, and \textbackslash mathrm\{d\} y as Mathpix escapes it, with \frac{1}{2} and \alpha.

        $$
        \'e+\^x=\"u
        $$

        \end{document}
        """;

    internal static byte[] NotesZip()
    {
        var id = MathpixTexZipTests.PdfId;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        using (var w = new StreamWriter(zip.CreateEntry($"{id}/{id}.tex").Open(), Encoding.UTF8))
            w.Write(NotesTex);
        return ms.ToArray();
    }

    /// <summary>The bulk import's placement of one chunk into its document (PdfImportPipeline), as the launch bot's ran.</summary>
    private async Task<(IServiceScope Scope, Guid DocumentId, Guid SessionId)> PlaceChunkAsync()
    {
        var documentId = Guid.NewGuid();
        var placeholder = Guid.NewGuid();
        await using (var db = CreateDbContext())
        {
            await db.Database.ExecuteSqlInterpolatedAsync($@"
                INSERT INTO documents (id, owner_id, title, created_at, updated_at)
                VALUES ({documentId}, {_userId}, {"Rostov notes"}, {DateTime.UtcNow}, {DateTime.UtcNow})");
            db.Blocks.Add(new Block
            {
                Id = placeholder, DocumentId = documentId, Type = "paragraph", SortOrder = 0,
                Content = JsonDocument.Parse("""{"text":"Pages 1–10: being read…"}"""),
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }
        var scope = Fixture.Factory.Services.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IPdfImportPipeline>();
        var placed = await pipeline.PlaceLatexProjectAsync(_userId, "rostov-notes.pdf", documentId, placeholder, applyPreamble: true, NotesZip(), null);
        return (scope, documentId, placed.SessionId);
    }

    [Fact]
    public async Task A_placed_chunk_keeps_its_Cyrillic_has_maths_accents_and_no_textbackslash()
    {
        var (scope, documentId, sessionId) = await PlaceChunkAsync();
        using var _ = scope;
        await using var db = CreateDbContext();
        var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId).OrderBy(b => b.SortOrder).ToListAsync();
        var texts = blocks.Select(b => b.Content.RootElement.GetRawText()).ToList();
        texts.Should().NotContain(t => t.Contains("being read"), "the placeholder was replaced");
        texts.Should().Contain(t => t.Contains("плоском"), "Cyrillic text is never touched");
        texts.Should().NotContain(t => t.Contains(@"\\'e") || t.Contains(@"\\^x"), "no text accent is left in maths");
        texts.Should().Contain(t => t.Contains(@"\\acute{e}")).And.Contain(t => t.Contains(@"\\hat{x}"));
        texts.Should().NotContain(t => t.Contains("textbackslash"));
        texts.Should().Contain(t => t.Contains(@"$\\mathrm{d}x$"));
        texts.Should().Contain(t => t.Contains(@"F_{\\text{трения}}"));
        // An accent maths prints the same way needs no warning.
        (await db.ImportDiagnostics.AsNoTracking().CountAsync(d => d.SessionId == sessionId && d.Code == "MathAccentWithoutLetter")).Should().Be(0);
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_the_placed_chunk_compiles_with_LuaLaTeX_and_prints_its_Cyrillic()
    {
        var (scope, documentId, _) = await PlaceChunkAsync();
        using var _s = scope;
        var exports = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IDocumentExportService>();
        var (pdf, engine, problems) = await exports.ExportToPdfWithProblemsAsync(documentId, "auto", null, false);
        Lilia.Api.Services.PdfPages.LooksLikePdf(pdf).Should().BeTrue();
        problems.Should().BeEmpty("the PDF is made in tolerant mode: it must have been made without a LaTeX error");
        engine.Should().Be("lualatex");
        var text = PdfText(pdf);
        text.Should().Contain("плоском").And.Contain("плоскость").And.Contain("трения", "a Cyrillic subscript prints as text")
            .And.NotContain("mathrm");

        // The editor's check (the export is tolerant: it makes a PDF in spite of an error): no error.
        var check = await (await CreateClientAs(_userId).PostAsync($"/api/latex/{documentId}/validate", null)).Content.ReadFromJsonAsync<JsonElement>();
        check.GetProperty("valid").GetBoolean().Should().BeTrue(check.ToString());
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_the_accessible_PDF_of_the_placed_chunk_is_made_by_LuaLaTeX_with_its_Cyrillic()
    {
        // A document with maths gets LaTeX's tagged PDF (TaggedPdf, LuaLaTeX): the Unicode faces must work there too.
        var (scope, documentId, _) = await PlaceChunkAsync();
        using var _s = scope;
        var exports = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IDocumentExportService>();
        var (pdf, engine) = await exports.ExportToPdfWithEngineAsync(documentId, "tagged");
        Lilia.Api.Services.PdfPages.LooksLikePdf(pdf).Should().BeTrue();
        engine.Should().Be("lualatex");
        PdfText(pdf).Should().Contain("плоском");
    }

    [Trait("Category", "RequiresTeX")]
    [Fact]
    public async Task For_real_a_document_written_by_hand_with_a_Cyrillic_heading_and_paragraph_compiles_in_LaTeX_and_Typst()
    {
        var client = CreateClientAs(_userId);
        var res = await client.PostAsJsonAsync("/api/documents", new { title = "Lecture notes" });
        res.EnsureSuccessStatusCode();
        var id = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var order = 0;
        foreach (var block in MixedDocument().Blocks)
            (await client.PostAsJsonAsync($"/api/documents/{id}/blocks",
                new { type = block.Type, content = block.Content.RootElement, sortOrder = order++ })).EnsureSuccessStatusCode();
        // An accent in an equation written by hand is LaTeX's to report, not changed: this one is right.
        (await client.PostAsJsonAsync($"/api/documents/{id}/blocks",
            new { type = "paragraph", content = new { text = @"The estimate is $\hat{x}$ and the length $\acute{e}$." }, sortOrder = order++ })).EnsureSuccessStatusCode();

        foreach (var engineHint in new[] { "latex", "pdflatex", "typst" })
        {
            var pdf = await client.GetAsync($"/api/documents/{id}/export/pdf?engine={engineHint}");
            pdf.StatusCode.Should().Be(HttpStatusCode.OK, $"{engineHint}: {await pdf.Content.ReadAsStringAsync()}");
            var used = pdf.Headers.TryGetValues("X-Render-Engine", out var e) ? e.First() : "";
            if (engineHint != "typst") used.Should().Be("lualatex", "asked for {0}, the Cyrillic needs LuaLaTeX", engineHint);
            PdfText(await pdf.Content.ReadAsByteArrayAsync()).Should().Contain("плоском", $"printed by {used}").And.Contain("плоскость");
        }
        var check = await (await client.PostAsync($"/api/latex/{id}/validate", null)).Content.ReadFromJsonAsync<JsonElement>();
        check.GetProperty("valid").GetBoolean().Should().BeTrue(check.ToString());
    }

    private static string PdfText(byte[] pdf)
    {
        var path = Path.Combine(Path.GetTempPath(), $"lilia-scan-compile-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, pdf);
        try
        {
            var p = Process.Start(new ProcessStartInfo("pdftotext", $"\"{path}\" -") { RedirectStandardOutput = true })!;
            var text = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            return text;
        }
        finally { File.Delete(path); }
    }
}
