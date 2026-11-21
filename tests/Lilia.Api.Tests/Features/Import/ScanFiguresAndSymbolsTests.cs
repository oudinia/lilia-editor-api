using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Lilia.Api.Tests.Integration.Infrastructure;
using Lilia.Import.Interfaces;
using Lilia.Import.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Lilia.Api.Tests.Features.Import;

/// <summary>
/// The launch bot's live import of a real handwritten page (20 Nov 2026, as PNG and as PDF): both imports finished, but
/// neither document compiled ("Unicode character خ (U+062E)": Mathpix read a symbol inside a formula as an Arabic letter),
/// the PDF's text held Mathpix's raw figure ids, and both lost the page's two sketches. Reproduced here with Mathpix's own
/// shapes:
/// <list type="bullet">
/// <item>a tex.zip whose figures are bare <c>\includegraphics[max width=\textwidth]{id}</c> (in a <c>center</c>
///   environment or not), its image in the zip, and خ inside <c>$…$</c> and a display formula;</item>
/// <item>a Markdown whose cropped figure link (cdn.mathpix.com) sits in the middle of a line, beside links Lilia must never
///   fetch (another site, a private address), and خ inside <c>$…$</c>.</item>
/// </list>
/// After the import: the formula holds a box instead, the block has a warning and the review a diagnostic naming it; each
/// figure is a figure block backed by an asset; no id or address is left in the text; nothing but cdn.mathpix.com is
/// fetched. Mathpix is <see cref="MathpixTexZipTests.FakeMathpix"/>.
/// </summary>
public class ScanFiguresAndSymbolsTests : IntegrationTestBase, IDisposable
{
    private const string Kha = "\u062E";
    private readonly List<WebApplicationFactory<Program>> _factories = [];
    private readonly string _userId = $"scanfig-{Guid.NewGuid():N}"[..28];

    public ScanFiguresAndSymbolsTests(TestDatabaseFixture fixture) : base(fixture) { }

    public void Dispose()
    {
        foreach (var f in _factories) f.Dispose();
    }

    public override async Task InitializeAsync()
    {
        await SeedUserAsync(_userId);
        await using var db = CreateDbContext();
        var pro = db.Plans.Single(p => p.Slug == "pro");
        db.UserPlans.Add(new Lilia.Core.Entities.UserPlan { Id = Guid.NewGuid(), UserId = _userId, PlanId = pro.Id, Status = "active", StartedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();
    }

    internal static readonly byte[] Sketch = ScanImageImportTests.Image(SkiaSharp.SKEncodedImageFormat.Jpeg, 120, 80);

    /// <summary>A tex.zip as Mathpix makes it for a handwritten page with two uncaptioned sketches.</summary>
    internal static byte[] HandwrittenTexZip()
    {
        var id = MathpixTexZipTests.PdfId;
        var tex = $$"""
            \documentclass[10pt]{article}
            \usepackage[utf8]{inputenc}
            \usepackage[T1]{fontenc}
            \usepackage{amsmath}
            \usepackage{amsfonts}
            \usepackage{amssymb}
            \usepackage{graphicx}
            \usepackage[export]{adjustbox}
            \graphicspath{ {./images/} }

            \begin{document}
            Charge distribution of an electron at $\vec{x}_{e}$: $\rho(\vec{x})=q \delta^{3}(\vec{x}-{{Kha}})$.

            \begin{center}
            \includegraphics[max width=\textwidth]{{{id}}-1}
            \end{center}

            Then the second sketch \includegraphics[max width=\textwidth, center]{{{id}}-2}

            $$
            \nabla^{2} \frac{1}{\left|\vec{x}-\vec{x}^{\prime}\right|}=-4 {{Kha}} \delta^{3}\left(\vec{x}-\vec{x}^{\prime}\right)
            $$

            \end{document}
            """;
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var w = new StreamWriter(zip.CreateEntry($"{id}/{id}.tex").Open(), Encoding.UTF8)) w.Write(tex);
            foreach (var n in new[] { 1, 2 })
                using (var s = zip.CreateEntry($"{id}/images/{id}-{n}.jpg").Open()) s.Write(Sketch);
        }
        return ms.ToArray();
    }

    /// <summary>Mathpix Markdown for the same page: a cropped figure mid-line, and links that must never be fetched.</summary>
    internal const string HandwrittenMarkdown = """
        Charge distribution of an electron at $\vec{x}_{e}$: $\rho=q \delta^{3}(\vec{x}-خ)$, read from the Markdown.

        The sketch ![](https://cdn.mathpix.com/cropped/2026_11_19_0a1b2c3d4e5f-1.jpg?height=200&width=300&top_left_y=10&top_left_x=20) shows it.

        ![](https://evil.example/steal.png)

        ![](http://169.254.169.254/latest/meta-data/x.png)
        """;

    private (WebApplicationFactory<Program> Factory, HttpClient Client) Host(MathpixTexZipTests.FakeMathpix api)
    {
        var factory = Fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(services =>
        {
            foreach (var d in services.Where(d => d.ServiceType == typeof(IPdfParser) || d.ServiceType == typeof(IScanImageParser)).ToList())
                services.Remove(d);
            services.AddScoped<IPdfParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
            services.AddScoped<IScanImageParser>(_ => MathpixTexZipTests.Service(api, MathpixTexZipTests.Settings()));
        }));
        _factories.Add(factory);
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-UserId", _userId);
        client.DefaultRequestHeaders.Add("X-Test-Email", $"{_userId}@lilia.test");
        client.DefaultRequestHeaders.Add("X-Test-Name", _userId);
        return (factory, client);
    }

    private static MathpixTexZipTests.FakeMathpix TexZipMathpix() => new()
    {
        TexZipDownload = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(HandwrittenTexZip()) },
    };

    private static MathpixTexZipTests.FakeMathpix MarkdownMathpix() => new()
    {
        TexZipStatuses = new(["error"]),
        MarkdownBody = HandwrittenMarkdown,
        CdnFigure = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Sketch) },
    };

    /// <summary>Imports with review, then finalizes: the review's warnings and diagnostics, and the document.</summary>
    private async Task<(Guid SessionId, Guid DocumentId)> ImportAsync(HttpClient client, string content, string format, string filename)
    {
        var res = await client.PostAsJsonAsync("/api/lilia/jobs/import", new { content, format, filename });
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        var sessionId = Guid.Parse((await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("reviewSessionId").GetString()!);
        var finalize = await client.PostAsJsonAsync($"/api/lilia/import-review/sessions/{sessionId}/finalize", new { force = true });
        finalize.StatusCode.Should().Be(HttpStatusCode.OK, await finalize.Content.ReadAsStringAsync());
        var documentId = (await finalize.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("document").GetProperty("id").GetGuid();
        return (sessionId, documentId);
    }

    private static readonly string FakePdf = Convert.ToBase64String(Encoding.ASCII.GetBytes("%PDF-1.4 fake"));
    private static readonly string ScanPng = Convert.ToBase64String(ScanImageImportTests.Image(SkiaSharp.SKEncodedImageFormat.Png, 65, 86));

    private async Task AssertCleanAsync(Guid sessionId, Guid documentId, int figures)
    {
        await using var db = CreateDbContext();
        var blocks = await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId).OrderBy(b => b.SortOrder).ToListAsync();
        var texts = blocks.Select(b => b.Content.RootElement.GetRawText()).ToList();

        // The unreadable symbol is a box in the formula; nowhere left in maths.
        texts.Should().NotContain(t => t.Contains(Kha));
        texts.Should().Contain(t => t.Contains(@"\\ensuremath{\\square}"));

        // Each figure is a figure block backed by an asset, and no id or address is text.
        var assets = await db.Assets.AsNoTracking().Where(a => a.DocumentId == documentId).ToListAsync();
        var figureBlocks = blocks.Where(b => b.Type == "figure").ToList();
        figureBlocks.Where(b => assets.Any(a => b.Content.RootElement.GetProperty("src").GetString() == a.Url)).Should().HaveCount(figures);
        blocks.Where(b => b.Type != "figure").Select(b => b.Content.RootElement.GetRawText())
            .Should().NotContain(t => t.Contains("2026_11_19_") || t.Contains("cdn.mathpix.com") || t.Contains("evil.example") || t.Contains("169.254"));

        // The review says which block had its symbol replaced.
        var diagnostics = await db.ImportDiagnostics.AsNoTracking().Where(d => d.SessionId == sessionId && d.Code == "UnreadableSymbolReplaced").ToListAsync();
        diagnostics.Should().NotBeEmpty();
        diagnostics.Should().OnlyContain(d => d.BlockId != null && d.Message.Contains("U+062E") && d.Message.Contains("replaced with a box"));
        var flagged = await db.ImportBlockReviews.AsNoTracking().Where(b => b.SessionId == sessionId && diagnostics.Select(d => d.BlockId).Contains(b.BlockId)).ToListAsync();
        flagged.Should().NotBeEmpty();
        flagged.Should().OnlyContain(b => b.Warnings != null && b.Warnings.RootElement.GetRawText().Contains("UnreadableSymbolReplaced"));
    }

    [Fact]
    public async Task A_PDF_read_from_Mathpix_s_tex_zip_keeps_both_sketches_as_figures_and_boxes_the_unreadable_symbol()
    {
        var api = TexZipMathpix();
        var (_, client) = Host(api);

        var (sessionId, documentId) = await ImportAsync(client, FakePdf, "PDF", "handwritten-page-1.pdf");

        await AssertCleanAsync(sessionId, documentId, figures: 2);
    }

    [Fact]
    public async Task A_PNG_scan_goes_the_PDF_s_way_and_keeps_its_sketches()
    {
        var api = TexZipMathpix();
        var (_, client) = Host(api);

        var (sessionId, documentId) = await ImportAsync(client, ScanPng, "IMAGE", "handwritten-page-1.png");

        api.Requests.First().Should().Be("POST api.mathpix.com/v3/pdf");
        await AssertCleanAsync(sessionId, documentId, figures: 2);
    }

    [Fact]
    public async Task From_the_Markdown_the_cropped_figure_is_fetched_from_Mathpix_s_CDN_only_and_nothing_else_is_ever_fetched()
    {
        var api = MarkdownMathpix();
        var (_, client) = Host(api);

        var (sessionId, documentId) = await ImportAsync(client, FakePdf, "PDF", "handwritten-page-1.pdf");

        api.Requests.Should().ContainSingle(r => r.StartsWith("GET cdn.mathpix.com/cropped/"));
        api.CdnRequestsWithKey.Should().Be(0, "the Mathpix key never travels with a figure request (#673)");
        api.Requests.Should().NotContain(r => r.Contains("evil.example") || r.Contains("169.254.169.254"), "no address but Mathpix's CDN is fetched");
        await AssertCleanAsync(sessionId, documentId, figures: 1);

        // The links Lilia didn't fetch are figure placeholders with a warning, not text.
        await using var db = CreateDbContext();
        var empty = await db.ImportBlockReviews.AsNoTracking().Where(b => b.SessionId == sessionId && b.OriginalType == "figure").ToListAsync();
        empty.Count(b => b.OriginalContent.RootElement.GetProperty("src").GetString() == "").Should().Be(2);
        (await db.ImportDiagnostics.AsNoTracking().CountAsync(d => d.SessionId == sessionId && d.Code == "FigureImageMissing")).Should().Be(2);
    }

    [Fact]
    public async Task A_cropped_figure_that_isn_t_an_image_or_fails_to_download_is_a_placeholder_figure_not_text()
    {
        var api = new MathpixTexZipTests.FakeMathpix
        {
            TexZipStatuses = new(["error"]),
            MarkdownBody = HandwrittenMarkdown,
            CdnFigure = () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>not an image</html>") },
        };
        var (_, client) = Host(api);

        var (sessionId, documentId) = await ImportAsync(client, FakePdf, "PDF", "handwritten-page-1.pdf");

        await using var db = CreateDbContext();
        (await db.Assets.AsNoTracking().CountAsync(a => a.DocumentId == documentId)).Should().Be(0);
        (await db.ImportDiagnostics.AsNoTracking().CountAsync(d => d.SessionId == sessionId && d.Code == "FigureImageMissing")).Should().Be(3);
        (await db.Blocks.AsNoTracking().Where(b => b.DocumentId == documentId && b.Type != "figure").ToListAsync())
            .Should().NotContain(b => b.Content.RootElement.GetRawText().Contains("cdn.mathpix.com"));
    }

    [Trait("Category", "RequiresTeX")]
    [Theory]
    [InlineData("tex.zip", "PDF")]
    [InlineData("tex.zip", "IMAGE")]
    [InlineData("markdown", "PDF")]
    public async Task For_real_the_imported_handwritten_page_compiles_to_a_PDF(string route, string format)
    {
        var api = route == "tex.zip" ? TexZipMathpix() : MarkdownMathpix();
        var (factory, client) = Host(api);

        var (_, documentId) = await ImportAsync(client, format == "PDF" ? FakePdf : ScanPng, format, format == "PDF" ? "page.pdf" : "page.png");

        using var scope = factory.Services.CreateScope();
        var exports = scope.ServiceProvider.GetRequiredService<Lilia.Api.Services.IDocumentExportService>();
        var (pdf, _) = await exports.ExportToPdfWithEngineAsync(documentId, "auto");
        Lilia.Api.Services.PdfPages.LooksLikePdf(pdf).Should().BeTrue();
    }

    [Theory]
    [InlineData("$x=\u062E$", "$x=\\ensuremath{\\square} $")]
    [InlineData("Text \u062E stays, $\\alpha+\u05D0\u05D1$ goes", "Text \u062E stays, $\\alpha+\\ensuremath{\\square} $ goes")]
    [InlineData("$\\aleph_0 + \\vec{x}\u20D7 + \u4E2D$", "$\\aleph_0 + \\vec{x}\u20D7 + \\ensuremath{\\square} $")]
    [InlineData("\\[ \u03B1 \\le \u0928 \\]", "\\[ \u03B1 \\le \\ensuremath{\\square}  \\]")]
    public void Only_letters_from_scripts_no_formula_can_hold_are_boxed_and_only_inside_maths(string text, string expected)
    {
        var replaced = new List<string>();
        OcrMathGuard.InText(text, replaced).Should().Be(expected);
        replaced.Should().NotBeEmpty();
    }

    [Fact]
    public void Greek_maths_symbols_and_text_outside_maths_are_never_touched()
    {
        var replaced = new List<string>();
        const string text = "αβγ ∇² ℏ ∫ $\\alpha \\nabla^2 ℏ ∑ ℵ$ النص العربي";
        OcrMathGuard.InText(text, replaced).Should().Be(text);
        OcrMathGuard.InMath(@"\nabla^{2} \psi = \hbar", replaced).Should().Be(@"\nabla^{2} \psi = \hbar");
        replaced.Should().BeEmpty();
    }

    [Fact]
    public void A_bare_includegraphics_becomes_a_figure_and_one_already_in_a_figure_is_left_alone()
    {
        var tex = "A\n\\begin{center}\n\\includegraphics[max width=\\textwidth]{a-1}\n\\end{center}\nB \\includegraphics{b-2} C\n"
            + "\\begin{figure}[h]\n\\begin{center}\n\\includegraphics{c-3}\n\\end{center}\n\\caption{Kept.}\n\\end{figure}\n";
        var result = MathpixFigures.AsFigureEnvironments(tex);
        result.Should().Contain("\\begin{figure}[h]\n\\centering\n\\includegraphics[max width=\\textwidth]{a-1}\n\\end{figure}");
        result.Should().Contain("\\begin{figure}[h]\n\\centering\n\\includegraphics{b-2}\n\\end{figure}");
        result.Should().Contain("\\begin{figure}[h]\n\\begin{center}\n\\includegraphics{c-3}\n\\end{center}\n\\caption{Kept.}\n\\end{figure}");
    }

    [Theory]
    [InlineData("https://cdn.mathpix.com/cropped/x.jpg", true)]
    [InlineData("https://CDN.mathpix.com/cropped/x.jpg", true)]
    [InlineData("http://cdn.mathpix.com/cropped/x.jpg", false)]
    [InlineData("https://cdn.mathpix.com.evil.example/x.jpg", false)]
    [InlineData("https://user@cdn.mathpix.com/x.jpg", false)]
    [InlineData("https://cdn.mathpix.com:8443/x.jpg", false)]
    [InlineData("https://169.254.169.254/x.jpg", false)]
    [InlineData("file:///etc/passwd", false)]
    public void Only_https_on_cdn_mathpix_com_may_be_fetched(string url, bool allowed) =>
        MathpixClient.IsMathpixImage(url).Should().Be(allowed);
}
